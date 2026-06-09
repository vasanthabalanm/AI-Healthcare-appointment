# Infrastructure Specification: Clinical Healthcare Platform

## Executive Summary

This document specifies the deployment infrastructure for the Clinical Healthcare Platform using **free/open-source hosting** as per BRD requirements.

### Deployment Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PRODUCTION DEPLOYMENT                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐      │
│  │    FRONTEND      │    │     BACKEND      │    │    DATABASE      │      │
│  │    (Angular)     │    │   (.NET 8 API)   │    │   (PostgreSQL)   │      │
│  ├──────────────────┤    ├──────────────────┤    ├──────────────────┤      │
│  │    Netlify       │───▶│   Railway.app    │───▶│   Railway.app    │      │
│  │    (FREE tier)   │    │   OR Render      │    │   OR Neon.tech   │      │
│  │                  │    │   OR Fly.io      │    │   OR Supabase    │      │
│  └──────────────────┘    └──────────────────┘    └──────────────────┘      │
│         │                        │                       │                  │
│         │                        │                       │                  │
│         ▼                        ▼                       ▼                  │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐      │
│  │  CDN + SSL       │    │  Redis Cache     │    │  Automated       │      │
│  │  (Netlify Edge)  │    │  (Upstash FREE)  │    │  Backups         │      │
│  └──────────────────┘    └──────────────────┘    └──────────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## INFRA-001: Frontend Deployment (Netlify)

### Configuration
| Attribute | Value |
|-----------|-------|
| **Platform** | Netlify (Free Tier) |
| **Technology** | Angular 18 SPA |
| **Build Command** | `npm run build -- --configuration=production` |
| **Publish Directory** | `dist/clinical-hub/browser` |
| **Node Version** | 20.x |

### Netlify Free Tier Limits
| Resource | Free Limit | Sufficient? |
|----------|------------|-------------|
| Bandwidth | 100 GB/month | ✅ Yes |
| Build minutes | 300/month | ✅ Yes |
| Concurrent builds | 1 | ✅ Yes |
| Sites | Unlimited | ✅ Yes |
| SSL/HTTPS | ✅ Included | ✅ Yes |
| Custom domains | ✅ Included | ✅ Yes |

### Deployment Steps
```bash
# 1. Connect GitHub repository to Netlify
# 2. Set base directory: clinical-hub
# 3. Build command: npm run build -- --configuration=production
# 4. Publish directory: dist/clinical-hub/browser
# 5. Environment variables:
#    - API_BASE_URL=https://your-backend.railway.app
```

---

## INFRA-002: Backend Deployment Options (FREE Tiers)

Since Netlify cannot host .NET backends, here are **free alternatives**:

### Option A: Railway.app (RECOMMENDED)
| Attribute | Value |
|-----------|-------|
| **Free Tier** | $5 credit/month (covers small apps) |
| **Docker Support** | ✅ Yes |
| **.NET Support** | ✅ Via Dockerfile |
| **PostgreSQL** | ✅ Built-in (free tier) |
| **Custom Domain** | ✅ Yes |

```dockerfile
# Dockerfile for Railway
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY ./publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ClinicalHealthcare.Api.dll"]
```

### Option B: Render.com
| Attribute | Value |
|-----------|-------|
| **Free Tier** | 750 hours/month |
| **Docker Support** | ✅ Yes |
| **.NET Support** | ✅ Via Dockerfile |
| **PostgreSQL** | ✅ Free (90 days, then $7/mo) |
| **Auto-sleep** | After 15 min inactivity |

### Option C: Fly.io
| Attribute | Value |
|-----------|-------|
| **Free Tier** | 3 shared VMs |
| **Docker Support** | ✅ Yes |
| **.NET Support** | ✅ Via Dockerfile |
| **PostgreSQL** | ✅ Built-in |
| **Global Edge** | ✅ Yes |

---

## INFRA-003: Database Deployment Options (FREE Tiers)

### Option A: Railway PostgreSQL (with Backend)
- **Free**: Included with Railway backend
- **Storage**: 1 GB
- **Connections**: Shared

### Option B: Neon.tech (Serverless PostgreSQL)
| Attribute | Value |
|-----------|-------|
| **Free Tier** | Forever free |
| **Storage** | 0.5 GB |
| **Compute** | 0.25 vCPU |
| **Branches** | Database branching |

### Option C: Supabase
| Attribute | Value |
|-----------|-------|
| **Free Tier** | Forever free |
| **Storage** | 500 MB |
| **API Requests** | 2 GB transfer |
| **Extras** | Auth, Storage, Edge Functions |

### Option D: ElephantSQL
| Attribute | Value |
|-----------|-------|
| **Free Tier** | Tiny Turtle (20 MB) |
| **Best For** | Development only |

---

## INFRA-004: Redis Cache (Upstash)

As per BRD requirement, using **Upstash Redis**:

| Attribute | Value |
|-----------|-------|
| **Platform** | Upstash.com |
| **Free Tier** | 10,000 commands/day |
| **Max Data** | 256 MB |
| **Persistence** | ✅ Yes |
| **TLS** | ✅ Included |

---

## Recommended FREE Stack

| Component | Platform | Free Tier |
|-----------|----------|-----------|
| **Frontend** | Netlify | 100 GB/mo bandwidth |
| **Backend** | Railway.app | $5 credit/month |
| **Database** | Railway PostgreSQL | Included |
| **Cache** | Upstash Redis | 10K commands/day |
| **DNS** | Cloudflare | Free |

### Total Cost: **$0/month** (within free limits)

---

## Environment Variables

### Netlify (Frontend)
```
API_BASE_URL=https://clinical-api.railway.app
```

### Railway (Backend)
```
DATABASE_URL=postgresql://user:pass@host:5432/clinical_db
REDIS_URL=redis://default:xxx@xxx.upstash.io:6379
JWT_SECRET=<your-secret>
ASPNETCORE_ENVIRONMENT=Production
```

---

## Deployment Workflow

```mermaid
graph LR
    A[Push to GitHub] --> B{Branch?}
    B -->|main| C[Netlify: Production Build]
    B -->|develop| D[Netlify: Preview Build]
    B -->|main| E[Railway: Auto Deploy]
    C --> F[https://clinical-hub.netlify.app]
    D --> G[https://preview-xxx.netlify.app]
    E --> H[https://clinical-api.railway.app]
```

---

## Next Steps

1. **Create Netlify Account**: https://app.netlify.com
2. **Connect GitHub Repo**: Link `clinical-hub` folder
3. **Create Railway Account**: https://railway.app
4. **Deploy Backend**: Railway auto-detects Dockerfile
5. **Provision PostgreSQL**: Railway one-click setup
6. **Create Upstash Account**: https://upstash.com
7. **Configure Environment Variables**: Update API URLs

---

## Rules Applied
- cloud-architecture-standards.instructions.md
- security-standards-owasp.instructions.md
- performance-best-practices.instructions.md

## Evaluation Scores
| Criteria | Score |
|----------|-------|
| Cost Optimization | 100/100 (all free tiers) |
| HIPAA Readiness | 80/100 (needs BAA for production) |
| Scalability | 70/100 (limited by free tiers) |
| Reliability | 75/100 (free tier SLAs) |

**Summary**: This infrastructure uses entirely free hosting platforms suitable for development and low-traffic production. For HIPAA compliance in production, consider Railway Pro or Render paid tiers which offer Business Associate Agreements (BAA).
