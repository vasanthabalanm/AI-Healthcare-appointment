# Business Requirements Document (BRD): Unified Patient Access & Clinical Intelligence Platform

## 1. Executive Summary
This project develops a unified, standalone healthcare platform that bridges the gap between patient scheduling and clinical data management. By combining a modern, patient-centric appointment booking system with a "Trust-First" clinical intelligence engine, the platform simplifies scheduling, reduces no-show rates, and eliminates the manual extraction of patient data from unstructured reports. The system serves patients, administrative staff, and admins, providing a seamless end-to-end data lifecycle from initial booking to post-visit data consolidation.

## 2. The Business Problem & Market Opportunity
Healthcare organizations suffer from a disconnected data pipeline that creates inefficiencies at multiple stages:

- **High No-Show Rates**: Providers experience up to a 15% no-show rate due to complex booking processes and lack of smart reminders, leading to revenue loss and underutilized schedules.
- **Manual Data Extraction**: Clinical staff spend 20+ minutes manually reading multi-format PDF reports to gather required patient data (vitals, history, meds), which is a primary bottleneck in clinical prep.
- **Market Gap**: Existing solutions are fragmented; booking tools lack clinical data context, and current AI coding tools face a "Black Box" trust deficit where users must manually verify unlinked data.

## 3. Proposed Solution
The platform is an intelligent, integration-ready aggregator designed to improve both operational scheduling and clinical prep.

- **Front-End Booking**: Intuitive scheduling with dynamic preferred slot swap, rule-based no-show risk assessment, and flexible digital intake (AI conversational or manual fallback).
- **Back-End Intelligence**: Ingests patient-uploaded historical documents and post-visit clinical notes to generate a unified, verified "360-Degree Patient View" equipped with extracted ICD-10 and CPT codes, transforming a 20-minute search task into a 2-minute verification action.

## 4. Core Features & Differentiators
- **Flexible Patient Intake**: Patients can choose between AI-assisted conversational intake or manual forms, with easy edits.
- **Dynamic Preferred Slot Swap**: Patients can book an available slot while selecting a preferred unavailable slot; if the preferred slot opens, the system automatically swaps appointments.
- **Centralized Staff Control**: Staff manage walk-ins, same-day queues, and arrivals. Patients cannot self-check in via apps or QR codes.
- **Data Consolidation & Conflict Resolution**: Aggregates multiple documents into a de-duplicated patient view, highlighting critical conflicts (e.g., conflicting medications).

## 5. Technology Stack & Infrastructure
- **UI (Frontend)**: Angular
- **API (Backend)**: .NET 8 (ASP.NET Core Web API)
- **Data Layer**: SQL Server
- **Hosting & Infrastructure**: Free, open-source-friendly platforms (Netlify, Vercel, GitHub Codespaces). Paid cloud hosting (AWS, Azure) is out of scope.
- **Auxiliary Tools**: Strictly free and open-source stacks for background processing and workflows.

## 6. Project Scope (Phase 1)

### In-Scope
- **User Roles**: Patients, Staff (front desk/call center), Admin (user management).
- **Booking & Reminders**: Appointment booking with waitlist, automated SMS/Email reminders, Google/Outlook calendar sync via free APIs. Appointment details sent as PDF via email.
- **Insurance Pre-Check**: Soft validation of insurance name and ID against dummy records.
- **Clinical Data Aggregation**: 360-Degree Data Extraction from uploaded clinical documents.
- **Medical Coding**: ICD-10 and CPT code mapping based on aggregated patient data.

### Out-of-Scope
- Provider logins or provider-facing actions
- Payment gateway integration
- Family member profile features
- Patient self-check-in
- Direct EHR integration or claims submission
- Paid cloud infrastructure

## 7. Non-Functional Requirements (NFRs)
- **Security & Compliance**: 100% HIPAA-compliant data handling, role-based access, immutable audit logging.
- **Infrastructure**: Native deployment (Windows Services/IIS), PostgreSQL for structured data, Upstash Redis for caching.
- **Reliability**: 99.9% uptime target, robust session management (15-minute timeout).

## 8. High-Level Success Criteria
- **Operational Efficiency**: Reduced no-show rates and staff time per appointment.
- **Platform Adoption**: High patient dashboard creation and appointment booking volume.
- **Clinical Accuracy**: AI-Human Agreement Rate >98% for clinical data and codes.
- **Risk Prevention**: Metrics of "Critical Conflicts Identified" to track prevented safety risks and claim denials.
