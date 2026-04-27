# Harness Engineering UI Design Document

## 1. Overview & Objectives

Following the successful implementation of the Harness Engineering backend features (Dynamic Pipelines, Schema Validation, Tool Use, Evaluation Framework), this document outlines the design for the accompanying Management UI. 

**Objectives:**
- Provide a visual, no-code/low-code interface to manage multi-agent workflows without manually editing JSON files on the server.
- Allow users to define, edit, and visualize dynamic pipelines.
- Manage JSON Schemas (for I/O validation) and Markdown prompt templates.
- **Implement "Guardrails" through independent Policy Management.**
- **Provide visibility into the AI's "Self-Correction" (retry) mechanisms.**
- **Enable "Knowledge Extraction" by converting evaluation failures into persistent rules.**
- **Integrate Git for version control of all AI environment configurations.**
- Integrate seamlessly into the existing AI Chat Pro V2 SPA architecture (HTMX + DaisyUI + Tailwind).

## 2. UI Architecture & Navigation

The Harness UI will be integrated into the existing main sidebar as a new primary section.

**New Sidebar Tab:**
- **Icon:** A gear/workflow icon.
- **Label:** `Harness`.

**Internal Tab Structure (Secondary Navigation):**
When the user clicks the Harness tab, the main panel will display a secondary tab/pills menu:
1. **Pipelines:** Manage `.json` files in `pipelines/`.
2. **Prompts:** Manage `.md` files in `pipelines/prompts/`.
3. **Schemas:** Manage `.json` files in `pipelines/schemas/`.
4. **Policies:** Manage `.md` files in `pipelines/policies/` (Environmental Constraints).
5. **Evaluations:** View metrics, scores, retry analysis, and agent step histories.

## 3. Screen Designs

### 3.1. Pipelines Manager (Pipelines Tab)
**Purpose:** Manage multi-agent pipeline configurations (`pipelines/*.json`).
- **List View:** A grid of available pipelines. Shows name, description, and stage count.
- **Action Buttons:** `[Create New]`, `[Edit]`, `[Delete]`, `[Set as Default]`.
- **Editor Modal/Panel:** 
  - A robust JSON text area that validates structural correctness on save.

### 3.2. Prompts, Schemas & Policies Managers
**Purpose:** Edit the underlying prompts, validation rules, and environmental constraints.
- **Prompts Tab:** Lists files in `pipelines/prompts/`. 
- **Schemas Tab:** Lists files in `pipelines/schemas/`.
- **Policies Tab (NEW):** Lists files in `pipelines/policies/`.
  - These are "Hard Guardrails" that the AI must follow across multiple stages.
  - CRUD functionality identical to Prompts.

### 3.3. Evaluation & Analytics Dashboard
**Purpose:** Provide visibility into the "self-correction" and "quality assessment" capabilities.
- **Overview Metrics:** Average Pipeline Success Rate, Average Retries per Stage.
- **Retry Analysis (NEW):** A pie chart or breakdown of "Why retries happen" (Schema Violation vs. Human Preference vs. Quality Fail).
- **Convert to Rule (NEW):** In the Evaluation detail view, a `[Extract as Rule]` button appears.
  - Clicking this triggers an AI-powered extraction that takes the error/failure and generates a Markdown policy draft in the **Policies** tab.

### 3.4. Git Control & History (NEW)
**Purpose:** Version control for "Harness" configurations.
- **Status Indicator:** Shows if there are uncommitted changes in the `pipelines/` directory.
- **Commit UI:** A small "Sync/Commit" button in the Harness header that opens a modal to enter a commit message.

## 4. Backend API Requirements

### Pipeline & Configuration Endpoints
- `GET /api/harness/pipelines` -> List all pipelines.
- `GET /api/harness/prompts` -> List prompts.
- `GET /api/harness/schemas` -> List schemas.
- **`GET /api/harness/policies` (NEW)** -> List policies.
- `POST /api/harness/save` -> Generic endpoint to save any harness file (Pipeline, Prompt, Schema, Policy).

### Git Endpoints (NEW)
- `GET /api/harness/git/status` -> Get `git status` for `pipelines/` directory.
- `POST /api/harness/git/commit` -> Execute `git add pipelines/ && git commit -m "..."`.

### Knowledge Extraction (NEW)
- **`POST /api/harness/extract-rule` (NEW)** -> Input an Evaluation ID; Output a suggested Markdown rule/policy.

## 5. Main Chat UI Integration (NEW)
**Purpose:** Visibility of the active "Harness" environment during chat.
- **Harness Status Badge:** A small section in the sidebar or chat header showing:
  - Active Pipeline: `Default`
  - Active Policies: `Security, Branding`
  - Current Stage: `Orchestrator` (visible only during generation)

## 6. Implementation Steps

1. **Backend Foundation:** Implement the required API endpoints for Policy CRUD and Git integration in `HarnessEndpoints.cs`.
2. **Rule Extraction Logic:** Add `ExtractRuleFromEvalAsync` to `MemoryConsolidationService.cs`.
3. **Frontend Wiring:** 
   - Add the "Policies" tab and Git controls to the Harness panel.
   - Update the Evaluations dashboard with the "Extract as Rule" button.
4. **Chat UI Update:** Add the "Environment Badge" to `wwwroot/index.html`.
