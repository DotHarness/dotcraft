---
layout: page
title: DotCraft
description: An AI agent that lives with the project — not with a single app.
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
        <p class="dc-hero__eyebrow t-stagger-line t-stagger-line--1">Project-scoped agent runtime</p>
        <h1 class="t-stagger-line t-stagger-line--2">An AI agent that lives with the <em>project.</em></h1>
        <p class="dc-hero__contrast t-stagger-line t-stagger-line--3">Not with a single app.</p>
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
          <img src="/dotcraft-logo.svg" alt="" />
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
            <figure><img src="/team-leader.svg" alt="" /><figcaption>Leader</figcaption></figure>
            <figure><img src="/team-explorer.svg" alt="" /><figcaption>Explorer</figcaption></figure>
            <figure><img src="/team-builder.svg" alt="" /><figcaption>Builder</figcaption></figure>
            <figure><img src="/team-reviewer.svg" alt="" /><figcaption>Reviewer</figcaption></figure>
            <figure><img src="/team-operator.svg" alt="" /><figcaption>Operator</figcaption></figure>
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
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">Beyond the desktop</p>
          <h2>Work keeps moving when the window closes.</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/entry-points/channels">Channels &amp; bots</a>
            <a class="dc-link" href="./features/agent-system/automations">Recurring work</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/channels.gif" alt="DotCraft keeping social channels connected in the background" loading="lazy" />
        </figure>
      </div>
    </article>
  </section>

  <section class="dc-thesis">
    <div class="dc-thesis__inner dc-reveal">
      <p class="dc-thesis__kicker">Why DotCraft</p>
      <p class="dc-thesis__quote">The <em>project</em> — not the client — is the unit of agent state and execution.</p>
      <div class="dc-loops">
        <div class="dc-loop">
          <span>01</span>
          <h3>Conversation</h3>
          <p>Persistent sessions, approvals, and queued input — continue from any client without starting over.</p>
        </div>
        <div class="dc-loop">
          <span>02</span>
          <h3>Work</h3>
          <p>Goals, Automations, Agent Teams, and isolated worktrees keep longer tasks moving under human control.</p>
        </div>
        <div class="dc-loop">
          <span>03</span>
          <h3>Memory</h3>
          <p>Reviewable project memory and history carry useful context into future conversations.</p>
        </div>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--cta dc-section--final">
    <div class="dc-cta dc-reveal">
      <h2>Ready to bring your project home?</h2>
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
