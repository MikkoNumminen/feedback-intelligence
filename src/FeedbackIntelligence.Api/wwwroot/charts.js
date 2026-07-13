"use strict";
// Shared chart module for the management view (index.html) and the desk
// segment (desk.html) — ONE implementation of the graphical layer (ADR-0026
// amendment), so the two pages can never drift. Built per the dataviz method:
// form first, color by job, palette validated by script.
//
// All text lands via textContent / SVG text nodes — hostile input must never
// reach innerHTML. The module injects its own CSS once; page theme tokens are
// referenced with fallbacks so either page's :root works.
window.FeedbackCharts = (() => {
  // Severity status palette, validated (lightness band, chroma floor, CVD
  // separation, contrast vs white) with the palette validator — do not
  // eyeball replacements. Keys follow the core severity enum; severities
  // outside the domain's declared set render in the neutral fallback and are
  // NEVER dropped from the distribution.
  const SEV_COLORS = { low: "#4e8ac8", medium: "#d97706", high: "#ef5350", critical: "#a02c2c" };
  const SEV_FALLBACK = "#9aa4af";
  // Magnitude bars and chart text ride the page's theme tokens.
  const BAR_HUE = "var(--acc, #0b5fa5)";
  const INK = "var(--ink, #1c2430)", MUT = "var(--mut, #66707d)";

  const CSS = `
  .kpi-row { display:grid; grid-template-columns:repeat(auto-fit, minmax(6.5rem, 1fr)); gap:.5rem; margin:.6rem 0; }
  .kpi { background:var(--card, #fff); border:1px solid var(--line, #d9dde3); border-radius:.5rem; padding:.45rem .6rem; }
  .kpi .kpi-label { font-size:.72rem; color:var(--mut, #66707d); }
  .kpi .kpi-value { font-size:1.35rem; font-weight:700; line-height:1.2; color:var(--ink, #1c2430); }
  .kpi .kpi-value.warn { color:var(--warn, #b3541e); }
  .chart-block { margin:.7rem 0; }
  .chart-title { font-size:.8rem; color:var(--mut, #66707d); margin:0 0 .3rem; }
  .chart-block svg { display:block; width:100%; height:auto; }
  .chart-block .bar { transition:opacity .12s; }
  .chart-block .bar:hover { opacity:.75; }
  .sev-legend { display:flex; flex-wrap:wrap; gap:.4rem .9rem; margin:.3rem 0 0; padding:0; list-style:none; font-size:.78rem; color:var(--mut, #66707d); }
  .sev-legend .swatch { display:inline-block; width:.62rem; height:.62rem; border-radius:.15rem; margin-right:.3rem; vertical-align:-.05rem; }`;

  let cssInjected = false;
  function injectCss() {
    if (cssInjected) return;
    const style = document.createElement("style");
    style.textContent = CSS;
    document.head.appendChild(style);
    cssInjected = true;
  }

  const svgNode = (tag, attrs) => {
    const n = document.createElementNS("http://www.w3.org/2000/svg", tag);
    for (const [k, v] of Object.entries(attrs || {})) n.setAttribute(k, v);
    return n;
  };

  function htmlNode(tag, className, textContent) {
    const n = document.createElement(tag);
    if (className) n.className = className;
    if (textContent !== undefined) n.textContent = textContent;
    return n;
  }

  // Rounded at the DATA end only (right), square at the baseline (left).
  const barPath = (x, y, w, h) => {
    const r = Math.min(4, w);
    return `M${x},${y} L${x + w - r},${y} Q${x + w},${y} ${x + w},${y + r} L${x + w},${y + h - r} Q${x + w},${y + h} ${x + w - r},${y + h} L${x},${y + h} Z`;
  };

  // Display order: the domain declares severities least→most severe (the
  // documented domain.json convention); any severity present in the data but
  // missing from the declaration appends after — the degraded case must stay
  // visible.
  function sevOrder(declared, sevCounts) {
    const base = declared && declared.length ? declared : Object.keys(SEV_COLORS);
    return [...base, ...Object.keys(sevCounts).filter(k => !base.includes(k))];
  }

  // Horizontal single-hue bar chart: entries per section. Direct value labels
  // at the tips; when a label cannot fit inside the plot it renders INSIDE
  // the bar end in white — the biggest number must stay readable.
  function categoryBarChart(sections) {
    const width = 320, rowH = 26, barH = 14, labelW = 118;
    const max = Math.max(...sections.map(s => s.count), 1);
    const plotW = width - labelW - 26;
    const svg = svgNode("svg", { viewBox: `0 0 ${width} ${sections.length * rowH}`, role: "img" });
    sections.forEach((s, i) => {
      const y = i * rowH + (rowH - barH) / 2;
      const w = Math.max((s.count / max) * plotW, 3);
      const name = svgNode("text", { x: labelW - 6, y: y + barH - 3, "text-anchor": "end", "font-size": "11", fill: MUT });
      name.textContent = s.label.length > 18 ? s.label.slice(0, 17) + "…" : s.label;
      svg.appendChild(name);
      const bar = svgNode("path", { d: barPath(labelW, y, w, barH), fill: BAR_HUE, class: "bar" });
      const tip = svgNode("title", {});
      tip.textContent = `${s.label} · ${s.count}`;
      bar.appendChild(tip);
      svg.appendChild(bar);
      const estimated = String(s.count).length * 7 + 4;
      const fitsOutside = labelW + w + 5 + estimated <= width;
      const value = fitsOutside
        ? svgNode("text", { x: labelW + w + 5, y: y + barH - 3, "font-size": "11", "font-weight": "600", fill: INK })
        : svgNode("text", { x: labelW + w - 5, y: y + barH - 3, "text-anchor": "end", "font-size": "11", "font-weight": "600", fill: "#fff" });
      value.textContent = s.count;
      svg.appendChild(value);
    });
    return svg;
  }

  // One horizontal 100% stacked bar over the ordered severity share, 2px
  // surface gaps between segments. Slivers clamp to a visible minimum and the
  // overflow is shaved off the widest segment so the stack never overlaps or
  // clips. The legend (mandatory at ≥2 series) carries identity + counts.
  function severityStackedBar(order, sevCounts, sevLabel) {
    const present = order.filter(k => (sevCounts[k] || 0) > 0);
    const total = present.reduce((sum, k) => sum + sevCounts[k], 0);
    if (total === 0) return null;
    const width = 320, barH = 16, gap = 2, minW = 2;
    const plotW = width - gap * (present.length - 1);
    const widths = present.map(k => Math.max((sevCounts[k] / total) * plotW, minW));
    const overflow = widths.reduce((sum, w) => sum + w, 0) - plotW;
    if (overflow > 0) widths[widths.indexOf(Math.max(...widths))] -= overflow;
    const svg = svgNode("svg", { viewBox: `0 0 ${width} ${barH}`, role: "img" });
    let x = 0;
    present.forEach((k, i) => {
      const seg = svgNode("rect", { x, y: 0, width: widths[i], height: barH, rx: 2, fill: SEV_COLORS[k] || SEV_FALLBACK, class: "bar" });
      const tip = svgNode("title", {});
      tip.textContent = `${sevLabel(k)} · ${sevCounts[k]}`;
      seg.appendChild(tip);
      svg.appendChild(seg);
      x += widths[i] + gap;
    });
    return svg;
  }

  function kpiTile(label, value, warn) {
    const tile = htmlNode("div", "kpi");
    tile.appendChild(htmlNode("div", "kpi-label", label));
    tile.appendChild(htmlNode("div", "kpi-value" + (warn ? " warn" : ""), String(value)));
    return tile;
  }

  // The graphical block: KPI tiles + the two charts.
  // opts: { sections: [{label, count, flagged}], sevCounts: {key: n}, total,
  //         directionLabel?, declaredSeverities?, sevLabel(key), strings:
  //         { entriesTile, flaggedTile, trendTile, chartCategories, chartSeverity, itemsWord } }
  function render(container, opts) {
    if (!opts.total || !opts.sections.length) return;
    injectCss();
    const declared = opts.declaredSeverities && opts.declaredSeverities.length
      ? opts.declaredSeverities : Object.keys(SEV_COLORS);
    const order = sevOrder(declared, opts.sevCounts);
    // The declared tail is the severe pair (least→most severe convention);
    // guard domains with fewer than two levels.
    const severeKeys = declared.slice(-Math.min(2, declared.length));
    const severeCount = severeKeys.reduce((sum, k) => sum + (opts.sevCounts[k] || 0), 0);
    const flagged = opts.sections.reduce((sum, s) => sum + (s.flagged || 0), 0);

    const kpis = htmlNode("div", "kpi-row");
    kpis.appendChild(kpiTile(opts.strings.entriesTile, opts.total));
    kpis.appendChild(kpiTile(severeKeys.map(opts.sevLabel).join(" + "), severeCount, severeCount > 0));
    kpis.appendChild(kpiTile(opts.strings.flaggedTile + " ⚠", flagged, flagged > 0));
    if (opts.directionLabel) kpis.appendChild(kpiTile(opts.strings.trendTile, opts.directionLabel));
    container.appendChild(kpis);

    const catBlock = htmlNode("div", "chart-block");
    catBlock.appendChild(htmlNode("p", "chart-title", opts.strings.chartCategories));
    catBlock.appendChild(categoryBarChart(opts.sections));
    container.appendChild(catBlock);

    const sevBar = severityStackedBar(order, opts.sevCounts, opts.sevLabel);
    if (sevBar) {
      const sevBlock = htmlNode("div", "chart-block");
      sevBlock.appendChild(htmlNode("p", "chart-title", opts.strings.chartSeverity));
      sevBlock.appendChild(sevBar);
      const legend = htmlNode("ul", "sev-legend");
      for (const k of order.filter(k => (opts.sevCounts[k] || 0) > 0)) {
        const li = htmlNode("li");
        const swatch = htmlNode("span", "swatch");
        swatch.style.background = SEV_COLORS[k] || SEV_FALLBACK;
        li.appendChild(swatch);
        li.appendChild(document.createTextNode(`${opts.sevLabel(k)} ${opts.sevCounts[k]}`));
        legend.appendChild(li);
      }
      sevBlock.appendChild(legend);
      container.appendChild(sevBlock);
    }
  }

  return { render };
})();
