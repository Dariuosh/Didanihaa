import { LitElement, css, html } from 'lit';

class DidanihaaStatistic extends LitElement {
  static properties = { label: { type: String }, value: { type: String }, symbol: { type: String } };
  declare label: string;
  declare value: string;
  declare symbol: string;
  constructor() { super(); this.label = ''; this.value = '—'; this.symbol = ''; }
  static styles = css`
    :host{display:block;min-width:0;font-family:inherit}
    article{box-sizing:border-box;position:relative;display:flex;flex-direction:column;gap:17px;padding:23px 21px;border:1px solid var(--line,oklch(93.2587% 0.005189 247.880));border-radius:15px;background:var(--surface,oklch(100.0000% 0.000000 0.000));color:var(--ink,oklch(28.3812% 0.025408 248.739))}
    .label{font-size:11px;color:var(--muted,oklch(59.2514% 0.027796 246.437));line-height:1.8;padding-inline-end:20px}
    strong{font-size:32px;font-weight:600;color:var(--ink,oklch(34.3095% 0.039417 244.951));line-height:1.2}
    .symbol{position:absolute;inset-inline-end:18px;top:21px;font-size:23px;color:var(--accent,oklch(65.1334% 0.122086 60.281))}
    @media(max-width:1100px){article{padding:18px 13px}.symbol{display:none}.label{padding:0}}
  `;
  render() { return html`<article><span class="symbol" aria-hidden="true">${this.symbol}</span><span class="label">${this.label}</span><strong>${this.value}</strong></article>`; }
}

class DidanihaaStatus extends LitElement {
  static properties = { label: { type: String }, tone: { type: String } };
  declare label: string;
  declare tone: string;
  constructor() { super(); this.label = ''; this.tone = 'warn'; }
  static styles = css`
    :host{display:inline-block}
    span{display:inline-flex;align-items:center;gap:7px;border-radius:8px;padding:8px 11px;font-size:11px;white-space:nowrap;color:var(--status-warning-ink,oklch(53.3685% 0.088087 72.494));background:var(--status-warning-bg,oklch(96.9541% 0.023675 73.615))}
    span::before{content:'';width:6px;height:6px;border-radius:50%;background:currentColor}
    :host([tone=good]) span{color:var(--status-good-ink,oklch(45% .08 160));background:var(--status-good-bg,oklch(96% .025 160))}
  `;
  render() { return html`<span>${this.label}</span>`; }
}

customElements.define('didanihaa-stat', DidanihaaStatistic);
customElements.define('didanihaa-status', DidanihaaStatus);
