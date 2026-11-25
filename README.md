[![CI](https://github.com/nate77143/ProductServiceApi/actions/workflows/ci.yml/badge.svg)](https://github.com/nate77143/ProductServiceApi/actions/workflows/ci.yml)

# Product Service API

A .NET 8 Web API serves a paginated product catalog from SQL Server. Both versions accept `pageNumber` and `pageSize`, defaulting to page 1 and three products per page. Products are ordered by ID.

| Endpoint | Access | Response |
| --- | --- | --- |
| `GET /api/v1/products` | Anonymous, to preserve access for existing catalog clients | Array of products |
| `GET /api/v2/products` | Registered clients with a bearer token carrying the `productApi` scope | Object with `totalCount` across all pages and a `products` array for the requested page |

Each product has `id`, `name`, `price`, `description`, and `quantity`. Requesting a page beyond the catalog returns an empty product array.

## Run with Docker Compose

Use Docker with Linux containers on an x86-64 host. From the repository root, create a local `.env` once. This Bash example uses OpenSSL to generate independent random credentials. Keep the file for subsequent runs so SQL Server can reuse its password and data volume.

```bash
(umask 077; set -C; cat > .env <<EOF
MSSQL_SA_PASSWORD=Aa1-$(openssl rand -hex 24)
JWT_SIGNING_KEY=$(openssl rand -hex 32)
PRODUCT_CLIENT_SECRET=$(openssl rand -hex 32)
EOF
)
docker compose up --build -d
docker compose logs -f api
```

Compose starts SQL Server Developer Edition, waits for it to accept queries, then starts the API. On first startup the API applies the database migration and seeds three products. The API listens on loopback port 8080. Use `docker compose down` to stop the stack and retain the database.

## Read products and request a token

These Bash examples use curl and jq. Run them from the directory containing the generated `.env`.

```bash
base=http://localhost:8080
curl -fsS "$base/api/v1/products?pageNumber=1&pageSize=2"

source .env
token=$(jq -n --arg secret "$PRODUCT_CLIENT_SECRET" \
  '{clientId:"productClient",clientSecret:$secret}' |
  curl -fsS "$base/auth/token" -H 'Content-Type: application/json' --data-binary @- |
  jq -er '.access_token')

curl -fsS "$base/api/v2/products?pageNumber=1&pageSize=2" \
  -H "Authorization: Bearer $token"
```

`POST /auth/token` takes JSON fields `clientId` and `clientSecret`. Valid credentials return `access_token`, `token_type` (`Bearer`), and `expires_in` (3600 seconds). The API signs tokens with HS256 and validates the signature, issuer, audience, and expiration. Invalid credentials or a missing bearer token on v2 return HTTP 401. Use HTTPS when exposing the API beyond loopback.

## Run with the .NET SDK

Copy `ProductServiceApi/ProductServiceApi/appsettings.Example.json` to `appsettings.json` in the same directory. Configure `ConnectionStrings:ProductDb` for a SQL Server instance and set `Jwt:ClientSecret` and `Jwt:SigningKey` to independent random secrets. The signing key must contain at least 32 UTF-8 bytes. Environment variables such as `Jwt__SigningKey` override file settings.

```sh
dotnet run --project ProductServiceApi/ProductServiceApi
dotnet test ProductServiceApi/ProductServiceApi.sln
```

The launch profile uses `https://localhost:5001`. The integration tests run the HTTP pipeline with an isolated in-memory catalog and generated test credentials.
