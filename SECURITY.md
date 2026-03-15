# Security Policy

## Supported versions

This project is in active early development. Security fixes are expected to land on the default branch first.

| Version | Supported |
| --- | --- |
| Default branch | Yes |
| Older snapshots and forks | No |

## Reporting a vulnerability

Please **do not** open a public GitHub issue for suspected security vulnerabilities.

Use one of these private paths instead:
- GitHub **Private Vulnerability Reporting**, if it is enabled for this repository
- A private GitHub contact method for the repository owner or maintainers if private vulnerability reporting is not available

When you report an issue, include:
- a clear description of the vulnerability
- affected files, endpoints, or components
- steps to reproduce, proof of concept, or logs where safe to share
- impact assessment and any suggested mitigations

Please do not include:
- live production secrets
- access tokens
- private user or customer data
- unnecessary exploit details in public channels

## Disclosure expectations

- We will try to acknowledge valid reports promptly.
- We may ask for clarification or reproduction details before confirming impact.
- Please allow time for investigation and remediation before any public disclosure.

## Scope notes

Because this repository integrates with LinkedIn OAuth, webhooks, and external APIs, security-sensitive reports may include:
- token handling or storage flaws
- webhook signature validation issues
- privilege or scope bypasses
- insecure defaults in local or production configuration
- data retention or deletion workflow failures

Reports about third-party services should be coordinated with the affected provider when appropriate.
