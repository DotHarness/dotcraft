---
layout: page
title: DotCraft
description: A project-native AI agent runtime for building extensible agents that evolve with your projects.
aside: false
sidebar: false
editLink: false
lastUpdated: false
---

<div class="dc-home">
  <section class="dc-hero">
    <div class="dc-hero__field" aria-hidden="true"><span></span><span></span><span></span></div>
    <div class="dc-hero__inner">
      <div class="dc-hero__copy t-stagger">
        <p class="dc-hero__eyebrow t-stagger-line t-stagger-line--1">Project-native agent runtime</p>
        <h1 class="t-stagger-line t-stagger-line--2">AI agents that evolve with your <em>projects.</em></h1>
        <p class="dc-hero__contrast t-stagger-line t-stagger-line--3">Built to extend.</p>
        <div class="dc-hero__cta t-stagger-line t-stagger-line--4">
          <div class="dc-actions">
            <a class="dc-button dc-button--primary" href="./getting-started">Get started</a>
            <div class="dc-download" data-download data-download-lang="en">
              <a class="dc-button dc-download__main" href="https://github.com/DotHarness/dotcraft/releases" data-download-main>Download release</a>
              <button class="dc-button dc-download__toggle" type="button" aria-haspopup="true" aria-expanded="false" aria-label="Choose platform" data-download-toggle>
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
              </button>
              <div class="dc-download__menu t-dropdown" role="menu" data-origin="top-left" hidden data-download-menu></div>
            </div>
          </div>
          <div class="dc-hero-install" data-install-tabs>
            <div class="dc-hero-install__tabs" role="tablist" aria-label="CLI installation platform">
              <button id="install-tab-windows" class="dc-hero-install__tab is-active" type="button" role="tab" aria-selected="true" aria-controls="install-windows" data-install-tab="windows">PowerShell</button>
              <button id="install-tab-unix" class="dc-hero-install__tab" type="button" role="tab" aria-selected="false" aria-controls="install-unix" data-install-tab="unix">macOS / Linux</button>
            </div>
            <div id="install-windows" class="dc-hero-install__command" role="tabpanel" aria-labelledby="install-tab-windows" data-install-panel="windows" data-command>
              <code>irm https://www.dotcraft.net/install.ps1 | iex</code>
              <button class="dc-cmd__copy" type="button" data-copy aria-label="Copy PowerShell install command">
                <span class="t-icon-swap" data-state="a">
                  <span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span>
                  <span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span>
                </span>
              </button>
            </div>
            <div id="install-unix" class="dc-hero-install__command" role="tabpanel" aria-labelledby="install-tab-unix" data-install-panel="unix" data-command hidden>
              <code>curl -fsSL https://www.dotcraft.net/install.sh | bash</code>
              <button class="dc-cmd__copy" type="button" data-copy aria-label="Copy macOS and Linux install command">
                <span class="t-icon-swap" data-state="a">
                  <span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span>
                  <span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span>
                </span>
              </button>
            </div>
          </div>
        </div>
      </div>
      <figure class="dc-hero__media">
        <div class="dc-hero__mascot" aria-hidden="true">
          <LiveMascot uid="hero" state="idle" interactive />
        </div>
        <div class="dc-demo" data-demo-lang="en">
          <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_banner.png" alt="DotCraft Desktop preview" />
        </div>
      </figure>
    </div>
  </section>

  <section id="features" class="dc-stories">
    <article class="dc-story">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">One project, one runtime</p>
          <h2>The project is the workspace.</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./getting-started">Getting Started</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/multi-workspace.gif" alt="Switching between projects in DotCraft Desktop" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">Agents and teams</p>
          <h2>Work with one agent, or a team of them.</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/agent-system/agent-profiles">Agent Profiles</a>
            <a class="dc-link" href="./features/agent-system/teams">Teams</a>
            <a class="dc-link" href="./features/agent-system/automations">Automations</a>
          </div>
          <div class="dc-story__team" aria-hidden="true">
            <figure><LiveMascot uid="team-leader" role="leader" state="idle" interactive /><figcaption>Leader</figcaption></figure>
            <figure><LiveMascot uid="team-explorer" role="explorer" state="watching" interactive /><figcaption>Explorer</figcaption></figure>
            <figure><LiveMascot uid="team-builder" role="builder" state="working" interactive /><figcaption>Builder</figcaption></figure>
            <figure><LiveMascot uid="team-reviewer" role="reviewer" state="thinking" interactive /><figcaption>Reviewer</figcaption></figure>
            <figure><LiveMascot uid="team-operator" role="operator" state="operating" interactive /><figcaption>Operator</figcaption></figure>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/agent-builder.gif" alt="Customizing a specialized agent through conversation" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">Built for applications</p>
          <h2>Bring the runtime into your own product.</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./developing/integrations/app-binding">DotCraft App</a>
            <a class="dc-link" href="./developing/sdks/">SDKs</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif" alt="The built-in Oratorio project board in DotCraft Desktop" loading="lazy" />
        </figure>
      </div>
    </article>
  </section>

  <section class="dc-thesis">
    <div class="dc-thesis__inner dc-reveal">
      <p class="dc-thesis__kicker">Agent Harness for .NET</p>
      <p class="dc-thesis__quote">Bring a complete agent runtime into any <em>.NET application.</em></p>
      <div class="dc-loops">
        <div class="dc-loop">
          <span>01</span>
          <h3>Runs where .NET runs</h3>
          <p>Embed the full runtime directly in desktop, server, CLI, or automation applications. No separate agent service to deploy or operate.</p>
        </div>
        <div class="dc-loop">
          <span>02</span>
          <h3>Built the .NET way</h3>
          <p>Use familiar Generic Host and dependency injection patterns. Your application stays in control of configuration, lifecycle, and user experience.</p>
        </div>
        <div class="dc-loop">
          <span>03</span>
          <h3>More than an agent loop</h3>
          <p>Durable sessions, tools, skills, approvals, and model providers are already composed. Start with your product, not the plumbing.</p>
        </div>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--cta dc-section--final">
    <div class="dc-cta dc-reveal">
      <div class="dc-cta__stage">
        <h2 data-cta-title>All set — over to you.</h2>
        <div class="dc-cta__mascot" aria-hidden="true"><LiveMascot uid="cta" state="idle" interactive /></div>
      </div>
      <div class="dc-hero__cta dc-cta__install">
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">Get started</a>
          <div class="dc-download" data-download data-download-lang="en">
            <a class="dc-button dc-download__main" href="https://github.com/DotHarness/dotcraft/releases" data-download-main>Download release</a>
            <button class="dc-button dc-download__toggle" type="button" aria-haspopup="true" aria-expanded="false" aria-label="Choose platform" data-download-toggle>
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
            </button>
            <div class="dc-download__menu t-dropdown" role="menu" data-origin="top-left" hidden data-download-menu></div>
          </div>
        </div>
        <div class="dc-hero-install" data-install-tabs>
          <div class="dc-hero-install__tabs" role="tablist" aria-label="CLI installation platform">
            <button id="footer-install-tab-windows" class="dc-hero-install__tab is-active" type="button" role="tab" aria-selected="true" aria-controls="footer-install-windows" data-install-tab="windows">PowerShell</button>
            <button id="footer-install-tab-unix" class="dc-hero-install__tab" type="button" role="tab" aria-selected="false" aria-controls="footer-install-unix" data-install-tab="unix">macOS / Linux</button>
          </div>
          <div id="footer-install-windows" class="dc-hero-install__command" role="tabpanel" aria-labelledby="footer-install-tab-windows" data-install-panel="windows" data-command>
            <code>irm https://www.dotcraft.net/install.ps1 | iex</code>
            <button class="dc-cmd__copy" type="button" data-copy aria-label="Copy PowerShell install command">
              <span class="t-icon-swap" data-state="a"><span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span><span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span></span>
            </button>
          </div>
          <div id="footer-install-unix" class="dc-hero-install__command" role="tabpanel" aria-labelledby="footer-install-tab-unix" data-install-panel="unix" data-command hidden>
            <code>curl -fsSL https://www.dotcraft.net/install.sh | bash</code>
            <button class="dc-cmd__copy" type="button" data-copy aria-label="Copy macOS and Linux install command">
              <span class="t-icon-swap" data-state="a"><span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span><span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span></span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </section>
</div>
