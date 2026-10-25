import unittest

from sonar_pr_compare import NotFound, compare

SERVER = "https://sonar.example"
MAIN = "Mixologist"
PR = "Mixologist-pr-7"
REVISION = "3f2c9a1e8b7d6c5f4a3b2c1d0e9f8a7b6c5d4e3f"


def issue(project, path, rule="csharpsquid:S1000", hash_="h1", line=10, key="k", message="Fix this.", status="OPEN"):
    return {
        "key": key,
        "rule": rule,
        "severity": "MAJOR",
        "component": f"{project}:{path}",
        "project": project,
        "line": line,
        "hash": hash_,
        "message": message,
        "issueStatus": status,
    }


class FakeSonar:
    def __init__(self, issues, coverage=None, page_size=500, pr_revisions=(REVISION,)):
        self.issues = issues
        self.coverage = coverage or {}
        self.pr_revisions = pr_revisions
        self.page_size = page_size
        self.requests = []

    def __call__(self, path, params):
        self.requests.append((path, params))
        if path == "/api/issues/search":
            statuses = params["issueStatuses"].split(",")
            project_issues = [i for i in self.issues.get(params["componentKeys"], []) if i["issueStatus"] in statuses]
            page = int(params["p"])
            start = (page - 1) * self.page_size
            return {
                "paging": {"pageIndex": page, "pageSize": self.page_size, "total": len(project_issues)},
                "issues": project_issues[start:start + self.page_size],
            }
        if path == "/api/measures/component":
            value = self.coverage.get(params["component"])
            measures = [] if value is None else [{"metric": "coverage", "value": value}]
            return {"component": {"key": params["component"], "measures": measures}}
        if path == "/api/project_analyses/search":
            assert params["project"] == PR
            if self.pr_revisions is None:
                raise NotFound(path)
            analyses = [{"key": f"A{n}", "revision": r} for n, r in enumerate(self.pr_revisions)]
            page_size = int(params["ps"])
            return {"paging": {"pageIndex": 1, "pageSize": page_size, "total": len(analyses)}, "analyses": analyses[:page_size]}
        raise AssertionError(f"unexpected request {path}")


def run(sonar):
    return compare(main_key=MAIN, pr_key=PR, revision=REVISION, public_url=SERVER, fetch=sonar)


class CompareTests(unittest.TestCase):
    def test_analysis_of_another_commit_fails_without_listing_issues(self):
        sonar = FakeSonar(
            {MAIN: [], PR: [issue(PR, "src/Web/Curator.cs", key="AX1")]},
            pr_revisions=("0a1b2c3d4e5f60718293a4b5c6d7e8f901234567", REVISION),
        )

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)
        self.assertIn("stale", summary)
        self.assertIn(REVISION, summary)
        self.assertIn("0a1b2c3d4e5f60718293a4b5c6d7e8f901234567", summary)
        self.assertNotIn("src/Web/Curator.cs", summary)

    def test_pr_project_that_does_not_exist_fails_as_missing(self):
        sonar = FakeSonar({MAIN: []}, pr_revisions=None)

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)
        self.assertIn("missing", summary)
        self.assertIn(REVISION, summary)

    def test_pr_project_without_an_analysis_fails(self):
        sonar = FakeSonar({MAIN: [], PR: []}, pr_revisions=())

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)
        self.assertIn("missing", summary)
        self.assertIn(REVISION, summary)
        self.assertNotIn("No new issues", summary)

    def test_issue_only_on_the_pr_is_new_and_fails(self):
        sonar = FakeSonar({
            MAIN: [],
            PR: [issue(PR, "src/Web/Curator.cs", line=42, key="AX1", message="Rename this property.")],
        })

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)
        self.assertIn("1 new issue", summary)
        self.assertIn("csharpsquid:S1000", summary)
        self.assertIn("MAJOR", summary)
        self.assertIn("src/Web/Curator.cs", summary)
        self.assertIn("42", summary)
        self.assertIn("Rename this property.", summary)
        self.assertIn("https://sonar.example/project/issues?id=Mixologist-pr-7&open=AX1", summary)

    def test_issue_that_only_moved_lines_is_not_new(self):
        sonar = FakeSonar({
            MAIN: [issue(MAIN, "src/Web/Curator.cs", line=10)],
            PR: [issue(PR, "src/Web/Curator.cs", line=55)],
        })

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 0)
        self.assertIn("No new issues", summary)

    def test_issue_fixed_in_the_pr_is_not_new(self):
        sonar = FakeSonar({
            MAIN: [issue(MAIN, "src/Web/Curator.cs")],
            PR: [],
        })

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 0)
        self.assertIn("No new issues", summary)

    def test_issue_triaged_away_on_main_is_not_new(self):
        sonar = FakeSonar({
            MAIN: [
                issue(MAIN, "src/Web/Error.cshtml.cs", hash_="accepted", status="ACCEPTED"),
                issue(MAIN, "src/Web/Shelf.cs", hash_="false-positive", status="FALSE_POSITIVE"),
            ],
            PR: [
                issue(PR, "src/Web/Error.cshtml.cs", hash_="accepted"),
                issue(PR, "src/Web/Shelf.cs", hash_="false-positive"),
            ],
        })

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 0)
        self.assertIn("No new issues", summary)

    def test_issue_fixed_on_main_that_returns_in_the_pr_is_new(self):
        sonar = FakeSonar({
            MAIN: [issue(MAIN, "src/Web/Curator.cs", status="FIXED")],
            PR: [issue(PR, "src/Web/Curator.cs")],
        })

        _, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)

    def test_same_rule_and_hash_in_another_file_is_new(self):
        sonar = FakeSonar({
            MAIN: [issue(MAIN, "src/Web/Curator.cs")],
            PR: [issue(PR, "src/Web/Shelf.cs")],
        })

        _, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)

    def test_external_rule_issues_never_count_as_new(self):
        sonar = FakeSonar({
            MAIN: [],
            PR: [issue(PR, "src/Web/Mixologist.Web.csproj", rule="external_osv:GHSA-1234")],
        })

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 0)
        self.assertNotIn("GHSA-1234", summary)

    def test_pages_through_every_issue_on_both_sides(self):
        shared = [issue(MAIN, f"src/File{n}.cs", hash_=f"h{n}") for n in range(5)]
        pr_side = [issue(PR, f"src/File{n}.cs", hash_=f"h{n}") for n in range(5)]
        pr_side.append(issue(PR, "src/Late.cs", hash_="late", key="LATE"))
        sonar = FakeSonar({MAIN: shared, PR: pr_side}, page_size=2)

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 1)
        self.assertIn("1 new issue", summary)
        self.assertIn("src/Late.cs", summary)
        self.assertNotIn("src/File4.cs", summary)

    def test_reports_both_coverages_without_failing_on_a_drop(self):
        sonar = FakeSonar({MAIN: [], PR: []}, coverage={MAIN: "93.5", PR: "80.1"})

        summary, exit_code = run(sonar)

        self.assertEqual(exit_code, 0)
        self.assertIn("93.5%", summary)
        self.assertIn("80.1%", summary)

    def test_missing_coverage_reads_as_unavailable(self):
        sonar = FakeSonar({MAIN: [], PR: []}, coverage={MAIN: "93.5"})

        summary, _ = run(sonar)

        self.assertIn("n/a", summary)


if __name__ == "__main__":
    unittest.main()
