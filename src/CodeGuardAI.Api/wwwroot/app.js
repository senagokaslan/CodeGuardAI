"use strict";

const state = {
  projects: [],
  selectedProject: null,
  review: null,
  findings: [],
  selectedFindingIds: new Set(),
  reviewController: null
};

const elements = {
  message: document.querySelector("#global-message"),
  projectForm: document.querySelector("#project-form"),
  projectName: document.querySelector("#project-name"),
  repositoryPath: document.querySelector("#repository-path"),
  refreshProjects: document.querySelector("#refresh-projects"),
  projectsState: document.querySelector("#projects-state"),
  projectList: document.querySelector("#project-list"),
  selectedProject: document.querySelector("#selected-project"),
  reviewForm: document.querySelector("#review-form"),
  reviewModel: document.querySelector("#review-model"),
  maxFindings: document.querySelector("#max-findings"),
  startReview: document.querySelector("#start-review"),
  cancelReview: document.querySelector("#cancel-review"),
  reviewStatus: document.querySelector("#review-status"),
  reviewProgress: document.querySelector("#review-progress"),
  scanSummary: document.querySelector("#scan-summary"),
  includedCount: document.querySelector("#included-count"),
  skippedCount: document.querySelector("#skipped-count"),
  includedBytes: document.querySelector("#included-bytes"),
  findingCount: document.querySelector("#finding-count"),
  severityFilter: document.querySelector("#severity-filter"),
  categoryFilter: document.querySelector("#category-filter"),
  visibleFindings: document.querySelector("#visible-findings"),
  findingsState: document.querySelector("#findings-state"),
  findingsList: document.querySelector("#findings-list"),
  selectedFindingsCount: document.querySelector("#selected-findings-count"),
  generateTests: document.querySelector("#generate-tests"),
  testsState: document.querySelector("#tests-state"),
  testsList: document.querySelector("#tests-list")
};

async function apiFetch(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers || {}) }
  });
  const contentType = response.headers.get("content-type") || "";
  const body = contentType.includes("json") ? await response.json() : null;
  if (!response.ok) {
    const validation = body?.errors
      ? Object.values(body.errors).flat().join(" ")
      : "";
    const detail = validation || body?.detail || body?.title || "The server could not complete the request.";
    const code = body?.code ? ` (${body.code})` : "";
    throw new Error(`${response.status}: ${detail}${code}`);
  }
  return body;
}

function showMessage(text, kind = "error") {
  elements.message.textContent = text;
  elements.message.className = `message ${kind}`;
  elements.message.hidden = false;
  elements.message.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function clearMessage() {
  elements.message.hidden = true;
  elements.message.textContent = "";
}

function setBusy(button, busy, busyLabel) {
  if (!button.dataset.label) button.dataset.label = button.textContent;
  button.disabled = busy;
  button.textContent = busy ? busyLabel : button.dataset.label;
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const unit = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / (1024 ** unit)).toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function makeElement(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

async function loadProjects() {
  elements.projectsState.hidden = false;
  elements.projectsState.textContent = "Loading projects…";
  elements.refreshProjects.disabled = true;
  try {
    const response = await apiFetch("/projects?page=1&pageSize=50");
    state.projects = response.items || [];
    renderProjects();
  } catch (error) {
    elements.projectsState.textContent = "Projects could not be loaded.";
    showMessage(`Could not load projects. ${error.message}`);
  } finally {
    elements.refreshProjects.disabled = false;
  }
}

function renderProjects() {
  elements.projectList.replaceChildren();
  if (state.projects.length === 0) {
    elements.projectsState.hidden = false;
    elements.projectsState.textContent = "No projects yet. Add a server-local repository above.";
    return;
  }
  elements.projectsState.hidden = true;
  state.projects.forEach(project => {
    const button = makeElement("button", "project-card");
    button.type = "button";
    button.dataset.projectId = project.id;
    button.setAttribute("aria-pressed", String(state.selectedProject?.id === project.id));
    if (state.selectedProject?.id === project.id) button.classList.add("selected");
    button.append(makeElement("strong", "", project.name));
    button.append(makeElement("small", "", project.repositoryPath));
    button.addEventListener("click", () => selectProject(project));
    elements.projectList.append(button);
  });
}

function selectProject(project) {
  state.selectedProject = project;
  state.review = null;
  state.findings = [];
  state.selectedFindingIds.clear();
  elements.selectedProject.textContent = `${project.name} · ${project.repositoryPath}`;
  elements.startReview.disabled = false;
  updateReviewStatus("Not started", "neutral");
  elements.scanSummary.hidden = true;
  resetResults();
  renderProjects();
}

function resetResults() {
  elements.severityFilter.replaceChildren(new Option("All severities", ""));
  elements.categoryFilter.replaceChildren(new Option("All categories", ""));
  elements.findingsList.replaceChildren();
  elements.findingsState.hidden = false;
  elements.findingsState.querySelector("strong").textContent = "No review results yet";
  elements.findingsState.querySelector("p").textContent = "Run a review for the selected project. Results will appear here without leaving the page.";
  elements.testsList.replaceChildren();
  elements.testsState.textContent = "Select one or more findings, then explicitly generate suggestions.";
  updateSelectionState();
  elements.visibleFindings.textContent = "0 visible";
}

async function createProject(event) {
  event.preventDefault();
  clearMessage();
  const button = elements.projectForm.querySelector("button[type='submit']");
  setBusy(button, true, "Adding…");
  try {
    const project = await apiFetch("/projects", {
      method: "POST",
      body: JSON.stringify({
        name: elements.projectName.value.trim(),
        repositoryPath: elements.repositoryPath.value.trim()
      })
    });
    state.projects = [project, ...state.projects.filter(item => item.id !== project.id)];
    elements.projectForm.reset();
    selectProject(project);
    showMessage("Project registered. The repository remains on the server machine.", "success");
  } catch (error) {
    showMessage(`Could not add project. ${error.message}`);
  } finally {
    setBusy(button, false, "Adding…");
  }
}

async function startReview(event) {
  event.preventDefault();
  if (!state.selectedProject) return;
  clearMessage();
  state.reviewController = new AbortController();
  setBusy(elements.startReview, true, "Reviewing…");
  elements.cancelReview.hidden = false;
  elements.reviewProgress.hidden = false;
  updateReviewStatus("Running", "running");
  resetResults();
  try {
    const review = await apiFetch("/reviews", {
      method: "POST",
      signal: state.reviewController.signal,
      body: JSON.stringify({
        projectId: state.selectedProject.id,
        model: elements.reviewModel.value.trim(),
        timeoutSeconds: 60,
        maxFindings: Number(elements.maxFindings.value)
      })
    });
    state.review = review;
    state.findings = review.findings || [];
    updateReviewStatus(review.status, review.status.toLowerCase());
    renderScanSummary(review);
    populateFilters();
    renderFindings();
    showMessage(`Review completed with ${state.findings.length} finding${state.findings.length === 1 ? "" : "s"}.`, "success");
  } catch (error) {
    if (error.name === "AbortError") {
      updateReviewStatus("Cancelled", "failed");
      showMessage("Review request cancelled. The server records a started run as Failed/Cancelled.");
    } else {
      updateReviewStatus("Failed", "failed");
      showMessage(`Review failed. ${error.message}`);
    }
  } finally {
    state.reviewController = null;
    elements.reviewProgress.hidden = true;
    elements.cancelReview.hidden = true;
    setBusy(elements.startReview, false, "Reviewing…");
    elements.startReview.disabled = !state.selectedProject;
  }
}

function updateReviewStatus(label, kind) {
  elements.reviewStatus.textContent = label;
  elements.reviewStatus.className = `status-chip ${["running", "completed", "failed"].includes(kind) ? kind : "neutral"}`;
}

function renderScanSummary(review) {
  const summary = review.scanSummary;
  if (!summary) {
    elements.scanSummary.hidden = true;
    return;
  }
  elements.includedCount.textContent = String(summary.includedFileCount);
  elements.skippedCount.textContent = String(summary.skippedEntryCount);
  elements.includedBytes.textContent = formatBytes(summary.includedBytes);
  elements.findingCount.textContent = String((review.findings || []).length);
  elements.scanSummary.hidden = false;
}

function populateFilters() {
  const severities = [...new Set(state.findings.map(item => item.severity))].sort();
  const categories = [...new Set(state.findings.map(item => item.category))].sort();
  elements.severityFilter.replaceChildren(new Option("All severities", ""));
  elements.categoryFilter.replaceChildren(new Option("All categories", ""));
  severities.forEach(value => elements.severityFilter.add(new Option(value, value)));
  categories.forEach(value => elements.categoryFilter.add(new Option(value, value)));
}

function renderFindings() {
  const severity = elements.severityFilter.value;
  const category = elements.categoryFilter.value;
  const visible = state.findings.filter(finding =>
    (!severity || finding.severity === severity) &&
    (!category || finding.category === category));

  elements.findingsList.replaceChildren();
  elements.visibleFindings.textContent = `${visible.length} visible`;
  if (visible.length === 0) {
    elements.findingsState.hidden = false;
    elements.findingsState.querySelector("strong").textContent = state.findings.length ? "No matching findings" : "No findings reported";
    elements.findingsState.querySelector("p").textContent = state.findings.length
      ? "Adjust the severity or category filters."
      : "The completed review did not report any grounded findings.";
    return;
  }

  elements.findingsState.hidden = true;
  visible.forEach(finding => elements.findingsList.append(createFindingCard(finding)));
}

function createFindingCard(finding) {
  const article = makeElement("article", "finding");
  const checkbox = makeElement("input", "finding-check");
  checkbox.type = "checkbox";
  checkbox.checked = state.selectedFindingIds.has(finding.id);
  checkbox.setAttribute("aria-label", `Select finding: ${finding.title}`);
  checkbox.addEventListener("change", () => {
    if (checkbox.checked) state.selectedFindingIds.add(finding.id);
    else state.selectedFindingIds.delete(finding.id);
    updateSelectionState();
  });

  const content = makeElement("div", "finding-content");
  const top = makeElement("div", "finding-topline");
  top.append(makeElement("span", `tag severity-${finding.severity.toLowerCase()}`, finding.severity));
  top.append(makeElement("span", "tag", finding.category));
  top.append(makeElement("span", "finding-path", `${finding.filePath}:${finding.startLine}-${finding.endLine}`));
  content.append(top);
  content.append(makeElement("h3", "", finding.title));
  content.append(makeElement("p", "", finding.reason));
  content.append(makeElement("p", "suggestion", `Suggestion: ${finding.suggestion}`));
  article.append(checkbox, content);
  return article;
}

function updateSelectionState() {
  const count = state.selectedFindingIds.size;
  elements.selectedFindingsCount.textContent = String(count);
  elements.generateTests.disabled = !state.review || state.review.status !== "Completed" || count === 0;
}

async function generateTests() {
  if (!state.review || state.selectedFindingIds.size === 0) return;
  clearMessage();
  setBusy(elements.generateTests, true, "Generating…");
  elements.testsState.textContent = "Generating bounded test suggestions…";
  elements.testsList.replaceChildren();
  try {
    const response = await apiFetch(`/api/reviews/${encodeURIComponent(state.review.id)}/tests`, {
      method: "POST",
      body: JSON.stringify({
        findingIds: [...state.selectedFindingIds],
        model: elements.reviewModel.value.trim(),
        timeoutSeconds: 60,
        maxSuggestions: 50
      })
    });
    renderTests(response.tests || []);
    showMessage("Test suggestions generated. No tests were executed and no repository files were changed.", "success");
  } catch (error) {
    elements.testsState.textContent = "Suggestions were not generated.";
    showMessage(`Test generation failed. ${error.message}`);
  } finally {
    setBusy(elements.generateTests, false, "Generating…");
    updateSelectionState();
  }
}

function renderTests(tests) {
  elements.testsList.replaceChildren();
  elements.testsState.textContent = tests.length
    ? `${tests.length} suggestion${tests.length === 1 ? "" : "s"} generated.`
    : "The model returned no test suggestions.";
  tests.forEach(test => {
    const article = makeElement("article", "test-card");
    article.append(makeElement("span", "tag", test.type));
    article.append(makeElement("h3", "", test.name));
    article.append(makeElement("p", "", `${test.target} · ${test.scenario}`));
    article.append(makeElement("p", "", test.reason));
    if (test.suggestedTestCode) article.append(makeElement("pre", "", test.suggestedTestCode));
    elements.testsList.append(article);
  });
}

elements.projectForm.addEventListener("submit", createProject);
elements.refreshProjects.addEventListener("click", loadProjects);
elements.reviewForm.addEventListener("submit", startReview);
elements.cancelReview.addEventListener("click", () => state.reviewController?.abort());
elements.severityFilter.addEventListener("change", renderFindings);
elements.categoryFilter.addEventListener("change", renderFindings);
elements.generateTests.addEventListener("click", generateTests);

loadProjects();
