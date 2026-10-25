#!/usr/bin/env python3
"""Run SonarScanner for .NET's begin or end step for the event the job runs on.

    sonar_scanner.py begin --scanner PATH --project-key KEY --host-url URL [--arguments TEXT]
    sonar_scanner.py end --scanner PATH

On a pull_request event the analysis goes into ``<KEY>-pr-<PR_NUMBER>`` with
SONAR_PR_TOKEN and waits for the quality gate, and end never fails; otherwise it
goes into ``KEY`` with SONAR_TOKEN. Reads GITHUB_EVENT_NAME, PR_NUMBER,
SONAR_TOKEN and SONAR_PR_TOKEN from the environment. --arguments holds extra
``/d:`` arguments for begin, one per line. Exits 2 on a missing token or a
refused argument, otherwise with the scanner's exit code.
"""

import argparse
import os
import subprocess
import sys
from dataclasses import dataclass

PULL_REQUEST_EVENT = "pull_request"
PROPERTY_PREFIX = "/d:"
CHOSEN_PROPERTIES = ("sonar.projectKey", "sonar.host.url", "sonar.token", "sonar.qualitygate.wait")
USAGE_ERROR = 2
SCANNER_NOT_FOUND = 127


class Refused(Exception):
    """A reason the action won't run the scanner, such as a missing token or an argument it chooses itself."""


@dataclass(frozen=True)
class Analysis:
    """Where the job's analysis goes: the pull request it belongs to, if any, and the token to send it with."""

    pr_number: str
    token: str

    @property
    def on_pull_request(self):
        """Whether the analysis is of a pull request, which gets a project of its own."""
        return bool(self.pr_number)

    @classmethod
    def for_event(cls, environ):
        """Choose the pull request's project and SONAR_PR_TOKEN on a pull_request event, else SONAR_TOKEN.

        Raises:
            Refused: The event's token, or a pull request's number, is empty.
        """
        if _on_pull_request(environ):
            analysis = cls(pr_number=environ.get("PR_NUMBER", ""), token=environ.get("SONAR_PR_TOKEN", ""))
            token_input = "pr-token"
            if not analysis.pr_number:
                raise Refused("A pull_request event needs PR_NUMBER, the pull request's number.")
        else:
            analysis = cls(pr_number="", token=environ.get("SONAR_TOKEN", ""))
            token_input = "token"
        if not analysis.token:
            raise Refused(f"The `{token_input}` input is empty, so there is no token to analyse with.")
        return analysis

    def project_key(self, main_key):
        """The project the analysis goes into: ``main_key``, or ``<main_key>-pr-<n>`` for a pull request."""
        return f"{main_key}-pr-{self.pr_number}" if self.on_pull_request else main_key


def _on_pull_request(environ):
    return environ.get("GITHUB_EVENT_NAME") == PULL_REQUEST_EVENT


def _parse(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    begin = commands.add_parser("begin")
    begin.add_argument("--scanner", required=True)
    begin.add_argument("--project-key", required=True)
    begin.add_argument("--host-url", required=True)
    begin.add_argument("--arguments", default="")
    end = commands.add_parser("end")
    end.add_argument("--scanner", required=True)
    return parser.parse_args(argv)


def main(argv, environ, run_scanner):
    """Run the scanner step ``argv`` names and return the job step's exit code.

    Args:
        argv: The command line after the script name.
        environ: The environment to read the event, pull request number and tokens from.
        run_scanner: Runs the scanner with a list of arguments and returns its exit code.

    Returns:
        The exit code for the step.
    """
    args = _parse(argv)
    if args.command == "end":
        return _end(args, environ, run_scanner)
    try:
        return _begin(args, environ, run_scanner)
    except Refused as refusal:
        print(refusal, file=sys.stderr)
        return USAGE_ERROR


def _begin(args, environ, run_scanner):
    analysis = Analysis.for_event(environ)
    return run_scanner([
        args.scanner, "begin",
        f"/k:{analysis.project_key(args.project_key)}",
        f"{PROPERTY_PREFIX}sonar.host.url={args.host_url}",
        f"{PROPERTY_PREFIX}sonar.token={analysis.token}",
        f"{PROPERTY_PREFIX}sonar.qualitygate.wait={str(analysis.on_pull_request).lower()}",
        *_caller_arguments(args.arguments),
    ])


def _end(args, environ, run_scanner):
    try:
        analysis = Analysis.for_event(environ)
    except Refused as refusal:
        return _end_failed(environ, USAGE_ERROR, str(refusal))
    exit_code = _run_or_report_missing(
        run_scanner, [args.scanner, "end", f"{PROPERTY_PREFIX}sonar.token={analysis.token}"]
    )
    return _end_failed(environ, exit_code, f"SonarScanner end exited {exit_code}.") if exit_code else 0


def _end_failed(environ, exit_code, reason):
    if _on_pull_request(environ):
        print(
            f"::warning::{reason} A pull request's quality gate judges a fresh project on nothing, "
            "so this step passes; sonar-pr-compare.yml is the pull request's gate."
        )
        return 0
    print(reason, file=sys.stderr)
    return exit_code


def _run_or_report_missing(run_scanner, arguments):
    try:
        return run_scanner(arguments)
    except FileNotFoundError:
        print(f"There is no scanner at {arguments[0]}; sonar-begin installs it.", file=sys.stderr)
        return SCANNER_NOT_FOUND


def _caller_arguments(text):
    arguments = [line.strip() for line in text.splitlines() if line.strip()]
    for argument in arguments:
        if not argument.startswith(PROPERTY_PREFIX):
            kind = argument.partition(":")[0]
            raise Refused(f"Only {PROPERTY_PREFIX} arguments can be passed to begin; got a `{kind}` one.")
        name = argument.removeprefix(PROPERTY_PREFIX).partition("=")[0]
        if name in CHOSEN_PROPERTIES:
            raise Refused(f"`{name}` is chosen by the action and can't be passed to begin.")
    return arguments


def _run_scanner(arguments):
    return subprocess.run(arguments, check=False).returncode


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:], os.environ, _run_scanner))
