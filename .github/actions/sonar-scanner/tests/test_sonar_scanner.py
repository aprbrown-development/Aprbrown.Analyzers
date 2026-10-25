import contextlib
import io
import unittest

from sonar_scanner import main

SCANNER = "/runner/temp/dotnet-sonarscanner/dotnet-sonarscanner"
HOST = "http://127.0.0.1:9000"
MAIN_TOKEN = "main-token"
PR_TOKEN = "pr-token"


class FakeScanner:
    def __init__(self, exit_code=0):
        self.exit_code = exit_code
        self.calls = []

    def __call__(self, arguments):
        self.calls.append(arguments)
        return self.exit_code


def push_environ(**overrides):
    return {"GITHUB_EVENT_NAME": "push", "SONAR_TOKEN": MAIN_TOKEN, "SONAR_PR_TOKEN": PR_TOKEN, **overrides}


def pull_request_environ(**overrides):
    return push_environ(GITHUB_EVENT_NAME="pull_request", PR_NUMBER="7", **overrides)


def begin(environ, scanner, arguments=""):
    return main(
        ["begin", "--scanner", SCANNER, "--project-key", "Mixologist", "--host-url", HOST, "--arguments", arguments],
        environ,
        scanner,
    )


class BeginTests(unittest.TestCase):
    def test_outside_a_pull_request_analyses_into_the_project_key_with_the_main_token(self):
        scanner = FakeScanner()

        exit_code = begin(push_environ(), scanner)

        self.assertEqual(exit_code, 0)
        self.assertEqual(scanner.calls, [[
            SCANNER, "begin",
            "/k:Mixologist",
            f"/d:sonar.host.url={HOST}",
            f"/d:sonar.token={MAIN_TOKEN}",
            "/d:sonar.qualitygate.wait=false",
        ]])

    def test_on_a_pull_request_analyses_into_its_own_project_with_the_pr_token_and_waits_for_the_gate(self):
        scanner = FakeScanner()

        exit_code = begin(pull_request_environ(), scanner)

        self.assertEqual(exit_code, 0)
        self.assertEqual(scanner.calls, [[
            SCANNER, "begin",
            "/k:Mixologist-pr-7",
            f"/d:sonar.host.url={HOST}",
            f"/d:sonar.token={PR_TOKEN}",
            "/d:sonar.qualitygate.wait=true",
        ]])

    def test_passes_each_line_of_the_callers_arguments_after_its_own_skipping_blank_lines(self):
        scanner = FakeScanner()
        arguments = (
            "/d:sonar.cs.opencover.reportsPaths=**/TestResults/**/coverage.opencover.xml\n"
            "\n"
            "  /d:sonar.exclusions=.claude/**,**/wwwroot/lib/**  \n"
        )

        begin(push_environ(), scanner, arguments)

        self.assertEqual(scanner.calls[0][-2:], [
            "/d:sonar.cs.opencover.reportsPaths=**/TestResults/**/coverage.opencover.xml",
            "/d:sonar.exclusions=.claude/**,**/wwwroot/lib/**",
        ])

    def test_refuses_caller_arguments_that_set_what_the_action_chooses_without_running_the_scanner(self):
        for argument in (
            "/k:Other",
            "/d:sonar.token=leaked",
            "/d:sonar.host.url=http://elsewhere",
            "/d:sonar.qualitygate.wait=false",
            "/d:sonar.projectKey=Other",
        ):
            with self.subTest(argument=argument):
                scanner = FakeScanner()

                exit_code = begin(pull_request_environ(), scanner, argument)

                self.assertEqual(exit_code, 2)
                self.assertEqual(scanner.calls, [])

    def test_fails_without_running_the_scanner_when_the_events_token_is_empty(self):
        for environ in (pull_request_environ(SONAR_PR_TOKEN=""), push_environ(SONAR_TOKEN="")):
            with self.subTest(event=environ["GITHUB_EVENT_NAME"]):
                scanner = FakeScanner()

                exit_code = begin(environ, scanner)

                self.assertEqual(exit_code, 2)
                self.assertEqual(scanner.calls, [])


def end(environ, scanner):
    return main(["end", "--scanner", SCANNER], environ, scanner)


class EndTests(unittest.TestCase):
    def test_outside_a_pull_request_ends_with_the_main_token_and_fails_as_the_scanner_does(self):
        scanner = FakeScanner(exit_code=1)

        exit_code = end(push_environ(), scanner)

        self.assertEqual(exit_code, 1)
        self.assertEqual(scanner.calls, [[SCANNER, "end", f"/d:sonar.token={MAIN_TOKEN}"]])

    def test_on_a_pull_request_ends_with_the_pr_token_and_passes_when_the_scanner_fails(self):
        scanner = FakeScanner(exit_code=1)

        with contextlib.redirect_stdout(io.StringIO()) as output:
            exit_code = end(pull_request_environ(), scanner)

        self.assertEqual(exit_code, 0)
        self.assertEqual(scanner.calls, [[SCANNER, "end", f"/d:sonar.token={PR_TOKEN}"]])
        self.assertIn("::warning::", output.getvalue())

    def test_on_a_pull_request_passes_without_a_token_or_a_scanner_to_run(self):
        def missing_scanner(arguments):
            raise FileNotFoundError(arguments[0])

        for environ, scanner in (
            (pull_request_environ(SONAR_PR_TOKEN=""), FakeScanner()),
            (pull_request_environ(), missing_scanner),
        ):
            with self.subTest(scanner=scanner), contextlib.redirect_stdout(io.StringIO()) as output:
                exit_code = end(environ, scanner)

                self.assertEqual(exit_code, 0)
                self.assertIn("::warning::", output.getvalue())

    def test_outside_a_pull_request_fails_when_there_is_no_scanner_to_run(self):
        def missing_scanner(arguments):
            raise FileNotFoundError(arguments[0])

        with contextlib.redirect_stderr(io.StringIO()):
            exit_code = end(push_environ(), missing_scanner)

        self.assertNotEqual(exit_code, 0)
