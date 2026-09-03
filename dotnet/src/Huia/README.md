# Huia

Core domain model, options tree, eventing abstractions and constants for the Huia multi-tenant
OpenID Connect / OAuth 2.0 Identity Provider suite.

This package is deliberately free of ASP.NET Core and Entity Framework Core dependencies. Add
`Huia.EntityFrameworkCore` for persistence and `Huia.AspNetCore` for the HTTP pipeline, OpenIddict
wiring and account UI.
