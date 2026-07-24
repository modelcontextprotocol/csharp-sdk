/*
 * Version picker widget for the multi-version MCP C# SDK docs site.
 *
 * Configuration is provided per-page via a `window.__DOCS__` object that
 * scripts/inject-version-picker.mjs writes into each page's <head>:
 *
 *   window.__DOCS__ = {
 *     version: "2.0",              // slug of the version this page belongs to
 *     base: "/",                   // site base path ("/" for a custom domain)
 *     default: "1.x",              // slug the site root redirects to
 *     versions: [                  // newest first; drives the dropdown
 *       { slug: "2.0", label: "2.0 (preview)", prerelease: true },
 *       { slug: "1.x", label: "1.x (latest)",  prerelease: false }
 *     ]
 *   };
 *
 * The pure helpers (joinPath / stripVersionPrefix / targetPath) are exported so
 * they can be unit-tested in Node without a DOM.
 */

export function joinPath(a, b) {
  const left = String(a || "").replace(/\/+$/, "");
  const right = String(b == null ? "" : b).replace(/^\/+/, "");
  return (left + "/" + right).replace(/\/{2,}/g, "/");
}

// Given the current pathname, return the part after "<base>/<currentSlug>/".
export function stripVersionPrefix(pathname, base, currentSlug) {
  const prefix = joinPath(joinPath(base, currentSlug), "");
  if (pathname.startsWith(prefix)) return pathname.slice(prefix.length);
  // Tolerate a missing trailing slash (e.g. "/2.0").
  const bare = joinPath(base, currentSlug);
  if (pathname === bare) return "";
  return "";
}

// Derive the deployment base from the page URL. This keeps picker navigation
// within project Pages sites, whose content is hosted below "/<repository>/".
export function getBasePath(cfg, pathname) {
  const marker = `/${cfg.version}/`;
  const markerIndex = pathname.indexOf(marker);
  if (markerIndex !== -1) return pathname.slice(0, markerIndex + 1) || "/";

  const bareMarker = `/${cfg.version}`;
  if (pathname.endsWith(bareMarker)) {
    return pathname.slice(0, -bareMarker.length + 1) || "/";
  }

  return cfg.base || "/";
}

// Compute the equivalent path in another version, preserving the sub-page.
export function targetPath(cfg, targetSlug, pathname) {
  const base = getBasePath(cfg, pathname);
  const sub = stripVersionPrefix(pathname, base, cfg.version);
  return joinPath(joinPath(base, targetSlug), sub);
}

function h(tag, attrs, children) {
  const el = document.createElement(tag);
  for (const k in attrs || {}) {
    if (k === "class") el.className = attrs[k];
    else el.setAttribute(k, attrs[k]);
  }
  for (const c of children || []) {
    el.appendChild(typeof c === "string" ? document.createTextNode(c) : c);
  }
  return el;
}

function versionRoot(cfg, slug, pathname) {
  return joinPath(joinPath(getBasePath(cfg, pathname), slug), "");
}

async function navigate(cfg, slug) {
  const candidate = targetPath(cfg, slug, location.pathname);
  const home = versionRoot(cfg, slug, location.pathname);
  // Best effort: keep the reader on the same page if it exists in the target
  // version; otherwise fall back to that version's landing page.
  try {
    const res = await fetch(candidate, { method: "HEAD" });
    if (res.ok) {
      location.assign(candidate + location.hash);
      return;
    }
  } catch (_) {
    /* HEAD may be blocked (e.g. file://) -- fall through to the version home. */
  }
  location.assign(home);
}

function previewBadge() {
  return h("span", { class: "dv-picker__badge" }, ["preview"]);
}

function buildPicker(cfg) {
  const current = cfg.versions.find((v) => v.slug === cfg.version);

  const select = h("select", {
    class: "dv-picker__select",
    "aria-label": "Select documentation version",
  });
  for (const v of cfg.versions) {
    const opt = h("option", { value: v.slug }, [v.label || v.slug]);
    if (v.slug === cfg.version) opt.selected = true;
    select.appendChild(opt);
  }
  select.addEventListener("change", () => navigate(cfg, select.value));

  const children = [h("span", { class: "dv-picker__label" }, ["Version"]), select];
  if (current && current.prerelease) {
    children.push(previewBadge());
  }
  return h("div", { class: "dv-picker" }, children);
}

function buildPrereleaseBanner() {
  return h("div", {
    class: "dv-prerelease-banner",
    role: "status",
  }, [
    "You are currently viewing",
    previewBadge(),
    "documentation.",
  ]);
}

function mount(picker) {
  const brand = document.querySelector(".navbar-brand");
  if (brand && brand.parentNode) {
    picker.classList.add("dv-picker--navbar");
    brand.parentNode.insertBefore(picker, brand.nextSibling);
    return;
  }
  picker.classList.add("dv-picker--floating");
  document.body.appendChild(picker);
}

function init() {
  const cfg = window.__DOCS__;
  if (!cfg || !Array.isArray(cfg.versions) || cfg.versions.length < 2) return;
  if (document.querySelector(".dv-picker, .dv-prerelease-banner")) return; // idempotent

  const current = cfg.versions.find((v) => v.slug === cfg.version);
  if (!current) return;
  if (current.prerelease) {
    document.body.prepend(buildPrereleaseBanner());
  }

  mount(buildPicker(cfg));
}

if (typeof document !== "undefined" && typeof document.createElement === "function") {
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
}
