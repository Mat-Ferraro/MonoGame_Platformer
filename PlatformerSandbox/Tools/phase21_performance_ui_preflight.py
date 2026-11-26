"""Compatibility entry point: legacy performance panel was retired in HK01."""
import unittest
from test_po_housekeeping import HousekeepingTests

if __name__ == '__main__':
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(HousekeepingTests))
    raise SystemExit(0 if result.wasSuccessful() else 1)
