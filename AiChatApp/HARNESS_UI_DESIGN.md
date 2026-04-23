# Harness Engineering UI Design Document

## 1. Overview & Objectives

Following the successful implementation of the Harness Engineering backend features (Dynamic Pipelines, Schema Validation, Tool Use, Evaluation Framework), this document outlines the design for the accompanying Management UI. 

**Objectives:**
- Provide a visual, no-code/low-code interface to manage multi-agent workflows without manually editing JSON files on the server.
- Allow users to define, edit, and visualize dynamic pipelines.
- Manage JSON Schemas (for I/O validation) and Markdown prompt templates.
- Display a comprehensive dashboard for quality evaluations, scoring, and self-correction (retry) history.
- Integrate seamlessly into the existing AI Chat Pro V2 SPA architecture (HTMX + DaisyUI + Tailwind).

## 2. UI Architecture & Navigation

The Harness UI will be integrated into the existing main sidebar as a new primary section, or as a sub-section within the "Settings" or "Projects" areas. Given its importance, a dedicated top-level tab is recommended.

**New Sidebar Tab:**
- **Icon:** A gear/workflow icon.
- **Label:** `Harness` or `Workflows`.

**Internal Tab Structure (Secondary Navigation):**
When the user clicks the Harness tab, the main panel will display a secondary tab/pills menu:
1. **Pipelines:** Manage `.json` files in `pipelines/`.
2. **Prompts:** Manage `.md` files in `pipelines/prompts/`.
3. **Schemas:** Manage `.json` files in `pipelines/schemas/`.
4. **Evaluations:** View metrics, scores, and AgentStep histories from the database.

## 3. Screen Designs

### 3.1. Pipelines Manager (Pipelines Tab)
**Purpose:** Manage multi-agent pipeline configurations (`pipelines/*.json`).
- **List View:** A grid of available pipelines. Shows name, description, and stage count.
- **Action Buttons:** `[Create New]`, `[Edit]`, `[Delete]`, `[Set as Default]`.
- **Editor Modal/Panel:** 
  - A visual builder or an advanced JSON editor (e.g., CodeMirror or a customized `textarea` with JSON syntax highlighting).
  - **Visual Builder Concept (Future iteration):** A drag-and-drop or form-based UI where users can add "Stages" (Orchestrator, Executor, Reviewer) and configure properties like `retryOnQualityFail`, `maxAttempts`, `outputSchema`, and `tools`.
  - **MVP:** A robust JSON text area that validates structural correctness on save.

### 3.2. Prompts & Schemas Managers
**Purpose:** Edit the underlying prompts and validation rules.
- **Prompts Tab:** Lists files in `pipelines/prompts/`. 
  - Allows inline editing of Markdown files.
  - Preview pane to see rendered markdown.
- **Schemas Tab:** Lists files in `pipelines/schemas/`.
  - Inline editing of JSON Schemas.
  - MVP: Raw text editor with JSON validation before saving.

### 3.3. Evaluation & Analytics Dashboard
**Purpose:** Provide visibility into the "self-correction" and "quality assessment" capabilities.
- **Overview Metrics:** Average Pipeline Success Rate, Average Retries per Stage, Average Evaluation Score (Accuracy, Safety, Format).
- **Recent Runs Table:** Lists recent pipeline executions (grouped by Session/Message).
  - Columns: Date, Pipeline Name, Stages Executed, Total Retries (Self-Correction events), Final Score.
- **Detail View:** Clicking a run shows the exact `AgentStep` logs.
  - Highlights where Schema Validation failed and the generated "correction prompt".
  - Displays the specific AI `Evaluation` results (0.0 - 1.0 scores) and the AI's `Reasoning` for that score.

## 4. Backend API Requirements

To support this UI, the following endpoints need to be added to `Program.cs` or a new dedicated controller/service:

### Pipeline Endpoints
- `GET /api/harness/pipelines` -> List all pipelines.
- `GET /api/harness/pipelines/{name}` -> Get pipeline JSON content.
- `POST /api/harness/pipelines` -> Save/Update pipeline JSON.
- `DELETE /api/harness/pipelines/{name}` -> Delete pipeline file.

### Schema & Prompt Endpoints
- `GET /api/harness/schemas` -> List schema files.
- `POST /api/harness/schemas/{name}` -> Save schema.
- `GET /api/harness/prompts` -> List prompt templates.
- `POST /api/harness/prompts/{name}` -> Save prompt template.

### Evaluation Endpoints
- `GET /api/harness/evaluations/summary` -> Get high-level stats.
- `GET /api/harness/evaluations/recent` -> Get latest evaluations joined with `AgentStep`.
- `GET /api/harness/evaluations/step/{stepId}` -> Get detailed evaluation for a specific step.

## 5. Implementation Steps

1. **Backend Foundation:** Implement the required API endpoints in `Program.cs` or a new minimal API group for reading/writing to the `pipelines/` directory and querying the `Evaluations` table.
2. **Frontend Wiring:** 
   - Add the "Harness" tab to the sidebar in `wwwroot/index.html`.
   - Add a new Swiper slide to the main content area for the Harness panel.
3. **UI Components (HTMX):**
   - Create HTML fragments for the Pipelines, Prompts, Schemas, and Evaluations sub-views.
   - Use HTMX to load these fragments dynamically when navigating the secondary menu.
4. **Editor Integration:** Integrate basic textareas or simple JS-based editors for JSON/MD editing.
5. **Dashboard Implementation:** Build the analytics dashboard using DaisyUI stats components and tables to visualize the Evaluation data.
6. **I18n:** Update `translations.js` with new keys for the Harness UI.
