# Pushed Authorization Requests (PAR)

Huia serves an [RFC 9126](https://www.rfc-editor.org/rfc/rfc9126) pushed-authorization endpoint at
`/{tenant}/connect/par`. A client `POST`s its authorization parameters there over a back channel and
gets a short-lived, opaque `request_uri` in return, which it then hands to `/connect/authorize`:

```http
POST /acme/connect/par
Content-Type: application/x-www-form-urlencoded

client_id=acme-web&response_type=code&redirect_uri=https://acme.example/cb
&scope=openid%20profile&code_challenge=…&code_challenge_method=S256
```

```json
{ "request_uri": "urn:ietf:params:oauth:request_uri:…", "expires_in": 90 }
```

```http
GET /acme/connect/authorize?client_id=acme-web&request_uri=urn:ietf:params:oauth:request_uri:…
```

The endpoint is advertised in each tenant's discovery document as
`pushed_authorization_request_endpoint`.

## Per-application

Every interactive client **may** use PAR — no configuration needed. To **require** it (a plain
`GET /connect/authorize` without a `request_uri` is then rejected), set the flag on the client:

```csharp
tenant.AddServerSideWebApplication("acme-web", "secret", client =>
{
    client.RequirePushedAuthorizationRequests = true;
    client.RedirectUris.Add(new Uri("https://acme.example/cb"));
});
```

or tick **Require PAR** when creating the client in the admin console. Under the hood this adds the
OpenIddict `ft:par` requirement to the application; the `ept:pushed_authorization` endpoint
permission is granted to every interactive client automatically.
