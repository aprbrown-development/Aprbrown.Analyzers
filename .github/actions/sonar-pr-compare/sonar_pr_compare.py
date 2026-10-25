#!/usr/bin/env python3
"""Report the open SonarQube issues a PR's project has that the main project doesn't.

    sonar_pr_compare.py --server URL --main KEY --pr KEY --revision SHA [--public-url URL]

Reads the token from SONAR_TOKEN. Prints a Markdown summary for
$GITHUB_STEP_SUMMARY and exits 1 when the PR project has any new issue or its
latest analysis isn't of the --revision commit, 2 when SonarQube can't be read
or answers in an unexpected shape.
"""

import argparse
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

PAGE_SIZE = 500
OPEN_STATUSES = "OPEN,CONFIRMED"
TRIAGED_STATUSES = "ACCEPTED,FALSE_POSITIVE"
EXTERNAL_RULE_PREFIX = "external_"
SUMMARY_HEADING = "## SonarQube: new issues"
HTTP_NOT_FOUND = 404


class NotFound(Exception):
    """Raised by a ``fetch`` when SonarQube has no such component."""


def compare(main_key, pr_key, revision, public_url, fetch):
    """Compare the open issues of the PR project against the main project.

    The comparison only runs when the PR project's latest analysis is of
    ``revision``; a missing or stale analysis fails without listing issues,
    since its issues are not those of the commit under review.

    An issue is new when its (rule, file path, line-content hash) is absent from
    main's open or triaged-away (accepted, false positive) issues, so code that
    only moved keeps its issues and a fresh PR project doesn't resurrect what
    main already dismissed. Issues from ``external_*`` rules are ignored. Coverage is reported for both sides but never fails.

    Args:
        main_key: Project key of the main branch's analysis.
        pr_key: Project key of the pull request's analysis.
        revision: Commit SHA the PR project's latest analysis must be of.
        public_url: Server URL that issue links in the summary point at.
        fetch: Callable ``(path, params) -> dict`` returning a decoded API response,
            raising ``NotFound`` when the component asked for doesn't exist.

    Returns:
        A ``(summary, exit_code)`` pair: the Markdown summary, and 1 when there is
        a new issue or the PR project's analysis is missing or stale, otherwise 0.
    """
    analysed_revision = _latest_revision(pr_key, fetch)
    if analysed_revision != revision:
        return _stale_summary(pr_key, revision, analysed_revision), 1
    main_issues = _issues(main_key, f"{OPEN_STATUSES},{TRIAGED_STATUSES}", fetch)
    main_fingerprints = {_fingerprint(issue) for issue in main_issues}
    pr_issues = _issues(pr_key, OPEN_STATUSES, fetch)
    new_issues = [issue for issue in pr_issues if _fingerprint(issue) not in main_fingerprints]
    coverage = {key: _coverage(key, fetch) for key in (main_key, pr_key)}
    summary = _summary(main_key, pr_key, public_url, new_issues, coverage)
    return summary, 1 if new_issues else 0


def _latest_revision(project_key, fetch):
    try:
        response = fetch("/api/project_analyses/search", {"project": project_key, "ps": "1"})
    except NotFound:
        return None
    analyses = response["analyses"]
    return analyses[0]["revision"] if analyses else None


def _issues(project_key, statuses, fetch):
    page = 1
    while True:
        response = fetch("/api/issues/search", {
            "componentKeys": project_key,
            "issueStatuses": statuses,
            "ps": str(PAGE_SIZE),
            "p": str(page),
        })
        for issue in response["issues"]:
            if not issue["rule"].startswith(EXTERNAL_RULE_PREFIX):
                yield issue
        paging = response["paging"]
        if paging["pageIndex"] * paging["pageSize"] >= paging["total"]:
            return
        page += 1


def _fingerprint(issue):
    return issue["rule"], _file_path(issue), issue.get("hash")


def _file_path(issue):
    return issue["component"].removeprefix(issue["project"] + ":")


def _coverage(project_key, fetch):
    response = fetch("/api/measures/component", {"component": project_key, "metricKeys": "coverage"})
    measures = response["component"].get("measures", [])
    return next((f"{m['value']}%" for m in measures if m["metric"] == "coverage"), "n/a")


def _stale_summary(pr_key, expected, found):
    state = "stale" if found else "missing"
    return "\n".join([
        SUMMARY_HEADING,
        "",
        f"**The analysis of `{pr_key}` is {state}**, so its issues can't be compared.",
        "",
        f"- Expected revision: `{expected}`",
        f"- Found revision: `{found or 'none'}`",
        "",
    ])


def _summary(main_key, pr_key, public_url, new_issues, coverage):
    lines = [SUMMARY_HEADING, ""]
    if new_issues:
        count = len(new_issues)
        lines += [
            f"**{count} new issue{'' if count == 1 else 's'}** in `{pr_key}` that `{main_key}` doesn't have.",
            "",
            "| Rule | Severity | File | Line | Message |",
            "|---|---|---|---|---|",
        ]
        for issue in sorted(new_issues, key=lambda issue: (_file_path(issue), issue.get("line", 0))):
            link = f"{public_url}/project/issues?id={urllib.parse.quote(pr_key)}&open={urllib.parse.quote(issue['key'])}"
            message = issue.get("message", "").replace("|", "\\|")
            lines.append(
                f"| {issue['rule']} | {issue.get('severity', '')} | `{_file_path(issue)}` "
                f"| {issue.get('line', '')} | [{message}]({link}) |"
            )
    else:
        lines.append(f"No new issues in `{pr_key}` compared with `{main_key}`.")
    lines += [
        "",
        "| Coverage | |",
        "|---|---|",
        f"| `{main_key}` | {coverage[main_key]} |",
        f"| `{pr_key}` | {coverage[pr_key]} |",
        "",
    ]
    return "\n".join(lines)


def _http_fetcher(server, token):
    credentials = base64.b64encode(f"{token}:".encode()).decode()

    def fetch(path, params):
        url = f"{server.rstrip('/')}{path}?{urllib.parse.urlencode(params)}"
        request = urllib.request.Request(url, headers={"Authorization": f"Basic {credentials}"})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == HTTP_NOT_FOUND:
                raise NotFound(path) from error
            raise

    return fetch


def main(argv=None):
    parser = argparse.ArgumentParser(description="Report the SonarQube issues a pull request introduces, failing on a stale analysis.")
    parser.add_argument("--server", required=True, help="SonarQube URL the API is called on")
    parser.add_argument("--main", required=True, help="main branch project key")
    parser.add_argument("--pr", required=True, help="pull request project key")
    parser.add_argument("--revision", required=True, help="commit SHA the PR project's analysis must be of")
    parser.add_argument("--public-url", help="SonarQube URL for links in the summary (default: --server)")
    args = parser.parse_args(argv)

    token = os.environ.get("SONAR_TOKEN")
    if not token:
        print("SONAR_TOKEN is not set", file=sys.stderr)
        return 2

    try:
        summary, exit_code = compare(
            main_key=args.main,
            pr_key=args.pr,
            revision=args.revision,
            public_url=(args.public_url or args.server).rstrip("/"),
            fetch=_http_fetcher(args.server, token),
        )
    except NotFound as error:
        print(f"SonarQube has no such component for {error}", file=sys.stderr)
        return 2
    except urllib.error.URLError as error:
        print(f"Couldn't read SonarQube: {error}", file=sys.stderr)
        return 2
    except (KeyError, ValueError) as error:
        print(f"SonarQube answered in an unexpected shape: {error!r}", file=sys.stderr)
        return 2

    print(summary)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
