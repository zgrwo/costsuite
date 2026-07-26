# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0.0 | :x:                |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, report them privately via GitHub's [Security Advisories](https://github.com/zgrwo/Bom-AddIn/security/advisories/new) feature, or email the maintainer directly.

### What to include

- A description of the vulnerability
- Steps to reproduce
- Affected module(s) and version(s)
- Any potential impact

### What to expect

- **Acknowledgment**: Within 48 hours
- **Status update**: Within 5 business days
- **Resolution timeline**: Depends on severity -- critical issues are prioritized for immediate patching

### Scope

This project is a cost-analysis Excel add-in handling sensitive BOM (Bill of Materials) and pricing data. Security considerations include:

- **BCrypt authentication**: User credentials are hashed with BCrypt (cost factor >= 12) before storage. No plaintext passwords are ever persisted or logged. Authentication tokens use cryptographically secure random generation.
- **DPAPI encryption**: Sensitive configuration values (connection strings, API keys) at rest are protected using Windows Data Protection API (DPAPI) with `DataProtectionScope.CurrentUser`, binding encrypted data to the current user account and machine.
- **AES-256 encryption**: Exported cost reports and cached data files are encrypted with AES-256-GCM, providing both confidentiality and integrity via authenticated encryption. Key material is derived via PBKDF2 with 100,000 iterations.
- **Audit logs**: All access to cost data, authentication events, and configuration changes are recorded in tamper-evident audit logs with SHA-256 chain hashing. Log entries include timestamp, user identity, action, and outcome.
- **Network isolation**: The add-in operates primarily offline. When network access is required (license validation, updates), connections use TLS 1.2+ with certificate pinning. No cost data is transmitted over the network without explicit user consent.
- **Offline-first architecture**: Core cost calculation and BOM analysis functions operate entirely offline. No cloud dependency exists for primary workflows, minimizing the attack surface and ensuring data sovereignty.

## Disclosure Policy

We follow coordinated disclosure. Once a fix is released, we will publish a security advisory crediting the reporter (unless anonymity is requested).
