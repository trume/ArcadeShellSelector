# Security Policy

## Supported Versions

Security fixes are provided for the latest published version on a best-effort basis.

| Version | Supported |
| ------- | --------- |
| Latest release | Yes |
| Older releases | No |

## Reporting a Vulnerability

Please do not report security issues in public GitHub issues.

Instead, contact the maintainer privately with:

- A clear description of the issue
- Affected version
- Reproduction steps or proof of concept
- Impact assessment
- Any suggested remediation

If you do not have a private contact channel set up yet, add one before making the repository broadly public.

## Project-Specific Notes

This project launches external executables, can wait on network paths, and may be used as a shell replacement on Windows systems. Reports involving process launching, path handling, privilege boundaries, or configuration file trust should be treated as security-sensitive.

Please allow reasonable time to investigate and prepare a fix before public disclosure.
