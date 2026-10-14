#!/usr/bin/env python3
"""Check packaged vehicle asset names and required Windows/Mac bundle outputs."""
import sys
from ftlib.package import validate_bundles as validate


if __name__ == '__main__':
    try:
        validate(sys.argv[1])
    except (ValueError, OSError) as exc:
        sys.exit(str(exc))
