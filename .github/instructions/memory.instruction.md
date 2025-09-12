---
applyTo: '**'
---
# User Memory

## User Preferences
- Programming languages: C# (.NET)
- Code style preferences: Utilize records for DTOs, DI for services, minimal controllers
- Development environment: VS Code dev container, Linux, bash
- Communication style: Concise, action-oriented progress updates

## Project Context
- Current project type: Web app + Azure Function for Power BI embedding
- Tech stack: .NET 8, Azure Functions v4, Bicep IaC, Power BI REST API
- Architecture patterns: MVC web app, function microservice for token generation, service layer abstraction
- Key requirements: Parameter-based workspace/report IDs, CSV-based authorization (user+location), service principal auth for Power BI, infra deployable via azd

## Coding Patterns
- Preferred patterns: Dependency Injection, records for immutable DTOs, segregated service layer for external calls
- Code organization preferences: Models in Models folder, services in Services, function logic self-contained
- Testing approaches: (Planned) Add unit tests for CSV authorization and token generation
- Documentation style: README with high-level flow + local + deploy instructions

## Context7 Research History
- (Not executed explicitly) Relied on known patterns for Power BI embed and Azure Functions

## Conversation History
- Implemented new function logic, web UI for embedding, infra bicep, azure.yaml, CSV authorization, environment variable driven config

## Notes
- Future improvements: Key Vault secret reference with managed identity, caching CSV, user groups RLS, unit tests.
