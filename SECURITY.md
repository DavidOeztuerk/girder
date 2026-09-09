# Security Policy

Girder carries the parts an application cannot get wrong: JWT verification,
password hashing, log masking, rate limiting and the egress boundary. A defect
in any of them is a defect in every application built on it.

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's private vulnerability reporting:

> Security → Report a vulnerability

That channel is private between you and the maintainer, and it exists so a
finding can be fixed before it is described in public.

If private reporting is unavailable to you, open an issue that says only
*"security finding, please provide a private channel"* — without details.

## What to expect

| | |
|---|---|
| First response | within 7 days |
| Assessment | within 14 days |
| Fix or mitigation | depends on severity, discussed with you |

Please give us time to ship a fix before publishing. You will be credited in
the release notes unless you prefer otherwise.

## Supported versions

Only the latest minor release of the current major version receives security
fixes. See the version policy in the README.

## Out of scope

Findings in applications that *use* Girder, unless they follow from Girder's
own behaviour or from documentation that led a reasonable developer astray.
