"""Host-side tests for the PO04B retry policy; no MonoGame or .NET required."""
import contextlib
import csv
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import po_run_benchmarks as runner


class RetryTests(unittest.TestCase):
    def test_only_clean_focus_cancellation_retries(self):
        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp)
            log = dest / 'process.log'
            log.write_text(runner.FOCUS_CANCEL + '\n', encoding='utf-8')
            self.assertTrue(runner.retryable_focus_cancel(dest, 0))
            self.assertFalse(runner.retryable_focus_cancel(dest, 1))
            self.assertFalse(runner.retryable_focus_cancel(dest, None))
            (dest/'partial.csv').touch()
            self.assertFalse(runner.retryable_focus_cancel(dest, 0))
            (dest/'partial.csv').unlink()
            (dest/'partial-metadata.txt').touch()
            self.assertFalse(runner.retryable_focus_cancel(dest, 0))
            (dest/'partial-metadata.txt').unlink()
            log.write_text('Unhandled exception', encoding='utf-8')
            self.assertFalse(runner.retryable_focus_cancel(dest, 0))

    def run_mock_suite(self, fail_count, retry_limit):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            project=root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
            project.parent.mkdir(parents=True); project.touch()
            count=0
            def attempt(c,dest,*args):
                nonlocal count
                count+=1
                failed=count<=fail_count
                (dest/'process.log').write_text(runner.FOCUS_CANCEL if failed else '',encoding='utf-8')
                r=dict(name=c['name'],status='failed' if failed else 'ok',case_folder=dest.name,wall_seconds=1)
                if failed: r['error']=runner.FOCUS_CANCEL
                else: r.update(fps=25,mean_ms=40,p95_ms=42)
                return r,failed
            argv=['runner','--po04b','--case','candidate_uncapped','--no-build','--focus-retries',str(retry_limit)]
            with patch.object(runner,'__file__',str(root/'Tools/po_run_benchmarks.py')), patch.object(runner.sys,'argv',argv), patch.object(runner.time,'sleep'), patch.object(runner,'run_attempt',side_effect=attempt), contextlib.redirect_stdout(io.StringIO()):
                rc=runner.main()
            archive=next((root/'Benchmarks').glob('*.zip'))
            with zipfile.ZipFile(archive) as z:
                names=z.namelist()
                rows=list(csv.DictReader(io.StringIO(z.read(next(n for n in names if n.endswith('/summary.csv'))).decode())))
                self.assertEqual(len(rows),1)
                self.assertEqual(len([n for n in names if n.endswith('/attempt_result.json')]),count)
                self.assertEqual(int(rows[0]['attempt']),count)
                return rc, count, rows[0]['status']

    def test_retry_then_success(self):
        self.assertEqual(self.run_mock_suite(1,3),(0,2,'ok'))

    def test_retry_limit(self):
        self.assertEqual(self.run_mock_suite(5,2),(1,3,'failed'))


if __name__=='__main__': unittest.main()
