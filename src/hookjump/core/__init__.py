"""Platform-independent core: pure functions over plain data.

Nothing here may import macOS, AppleScript, network, database or clock code
(enforced by tests/test_core_purity.py). All times are integer milliseconds.
The same rules are written down in spec/ALGORITHM.md, so another platform can
re-implement them without reading this code.
"""
