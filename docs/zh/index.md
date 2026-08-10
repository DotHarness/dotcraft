---
layout: page
title: DotCraft
description: 用于构建可扩展 AI Agent 的项目原生运行时，让 Agent 随项目持续演进。
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
        <p class="dc-hero__eyebrow t-stagger-line t-stagger-line--1">项目原生 Agent 运行时</p>
        <h1 class="t-stagger-line t-stagger-line--2">随<em>项目</em>持续演进的 AI Agent。</h1>
        <p class="dc-hero__contrast t-stagger-line t-stagger-line--3">为扩展而生。</p>
        <div class="dc-hero__cta t-stagger-line t-stagger-line--4">
          <div class="dc-actions">
            <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
            <div class="dc-download" data-download data-download-lang="zh">
              <a class="dc-button dc-download__main" href="https://github.com/DotHarness/dotcraft/releases" data-download-main>下载 Release</a>
              <button class="dc-button dc-download__toggle" type="button" aria-haspopup="true" aria-expanded="false" aria-label="选择平台" data-download-toggle>
                <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
              </button>
              <div class="dc-download__menu t-dropdown" role="menu" data-origin="top-left" hidden data-download-menu></div>
            </div>
          </div>
          <div class="dc-hero-install" data-install-tabs>
            <div class="dc-hero-install__tabs" role="tablist" aria-label="CLI 安装平台">
              <button id="install-tab-windows" class="dc-hero-install__tab is-active" type="button" role="tab" aria-selected="true" aria-controls="install-windows" data-install-tab="windows">PowerShell</button>
              <button id="install-tab-unix" class="dc-hero-install__tab" type="button" role="tab" aria-selected="false" aria-controls="install-unix" data-install-tab="unix">macOS / Linux</button>
            </div>
            <div id="install-windows" class="dc-hero-install__command" role="tabpanel" aria-labelledby="install-tab-windows" data-install-panel="windows" data-command>
              <code>irm https://www.dotcraft.net/install.ps1 | iex</code>
              <button class="dc-cmd__copy" type="button" data-copy aria-label="复制 PowerShell 安装命令">
                <span class="t-icon-swap" data-state="a">
                  <span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span>
                  <span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span>
                </span>
              </button>
            </div>
            <div id="install-unix" class="dc-hero-install__command" role="tabpanel" aria-labelledby="install-tab-unix" data-install-panel="unix" data-command hidden>
              <code>curl -fsSL https://www.dotcraft.net/install.sh | bash</code>
              <button class="dc-cmd__copy" type="button" data-copy aria-label="复制 macOS 和 Linux 安装命令">
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
        <div class="dc-demo" data-demo-lang="zh">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/desktop_banner.png" alt="DotCraft Desktop 预览" />
        </div>
      </figure>
    </div>
  </section>

  <section id="features" class="dc-stories">
    <article class="dc-story">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">一个项目，一个运行时</p>
          <h2>项目就是工作区。</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./getting-started">快速开始</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/multi-workspace.gif" alt="在 DotCraft Desktop 中切换项目" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">Agent 与团队</p>
          <h2>一个 Agent 独当一面，或一支团队协同作战。</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/agent-system/agent-profiles">Agent Profiles</a>
            <a class="dc-link" href="./features/agent-system/teams">Teams</a>
            <a class="dc-link" href="./features/agent-system/automations">自动化</a>
          </div>
          <div class="dc-story__team" aria-hidden="true">
            <figure><img src="/team-leader.svg" alt="" /><figcaption>领队</figcaption></figure>
            <figure><img src="/team-explorer.svg" alt="" /><figcaption>探索</figcaption></figure>
            <figure><img src="/team-builder.svg" alt="" /><figcaption>构建</figcaption></figure>
            <figure><img src="/team-reviewer.svg" alt="" /><figcaption>评审</figcaption></figure>
            <figure><img src="/team-operator.svg" alt="" /><figcaption>运维</figcaption></figure>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/agent-builder.gif" alt="通过对话定制一个专属 Agent" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">为应用而生</p>
          <h2>把运行时带进你自己的产品。</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./developing/integrations/app-binding">DotCraft App</a>
            <a class="dc-link" href="./developing/sdks/">SDK</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/desktop-extensions.gif" alt="DotCraft Desktop 内置的 Oratorio 项目看板" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">离开桌面之后</p>
          <h2>窗口关上，工作仍在继续。</h2>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/entry-points/channels">渠道与机器人</a>
            <a class="dc-link" href="./features/agent-system/automations">定时任务</a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/channels.gif" alt="DotCraft 在后台保持社交渠道连接" loading="lazy" />
        </figure>
      </div>
    </article>
  </section>

  <section class="dc-thesis">
    <div class="dc-thesis__inner dc-reveal">
      <p class="dc-thesis__kicker">为什么选 DotCraft</p>
      <p class="dc-thesis__quote"><em>项目</em>——而不是客户端——才是 Agent 状态与执行的单位。</p>
      <div class="dc-loops">
        <div class="dc-loop">
          <span>01</span>
          <h3>对话</h3>
          <p>持久的会话、审批与排队输入——从任何客户端继续，不必重来。</p>
        </div>
        <div class="dc-loop">
          <span>02</span>
          <h3>工作</h3>
          <p>Goals、自动化、Agent Teams 与隔离的 worktree，让长任务在人的掌控下持续推进。</p>
        </div>
        <div class="dc-loop">
          <span>03</span>
          <h3>记忆</h3>
          <p>可审阅的项目记忆与历史，把有用的上下文带进未来的对话。</p>
        </div>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--cta dc-section--final">
    <div class="dc-cta dc-reveal">
      <h2>准备好把项目接回家了吗？</h2>
      <div class="dc-hero__cta dc-cta__install">
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
          <div class="dc-download" data-download data-download-lang="zh">
            <a class="dc-button dc-download__main" href="https://github.com/DotHarness/dotcraft/releases" data-download-main>下载 Release</a>
            <button class="dc-button dc-download__toggle" type="button" aria-haspopup="true" aria-expanded="false" aria-label="选择平台" data-download-toggle>
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
            </button>
            <div class="dc-download__menu t-dropdown" role="menu" data-origin="top-left" hidden data-download-menu></div>
          </div>
        </div>
        <div class="dc-hero-install" data-install-tabs>
          <div class="dc-hero-install__tabs" role="tablist" aria-label="CLI 安装平台">
            <button id="footer-install-tab-windows" class="dc-hero-install__tab is-active" type="button" role="tab" aria-selected="true" aria-controls="footer-install-windows" data-install-tab="windows">PowerShell</button>
            <button id="footer-install-tab-unix" class="dc-hero-install__tab" type="button" role="tab" aria-selected="false" aria-controls="footer-install-unix" data-install-tab="unix">macOS / Linux</button>
          </div>
          <div id="footer-install-windows" class="dc-hero-install__command" role="tabpanel" aria-labelledby="footer-install-tab-windows" data-install-panel="windows" data-command>
            <code>irm https://www.dotcraft.net/install.ps1 | iex</code>
            <button class="dc-cmd__copy" type="button" data-copy aria-label="复制 PowerShell 安装命令">
              <span class="t-icon-swap" data-state="a"><span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span><span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span></span>
            </button>
          </div>
          <div id="footer-install-unix" class="dc-hero-install__command" role="tabpanel" aria-labelledby="footer-install-tab-unix" data-install-panel="unix" data-command hidden>
            <code>curl -fsSL https://www.dotcraft.net/install.sh | bash</code>
            <button class="dc-cmd__copy" type="button" data-copy aria-label="复制 macOS 和 Linux 安装命令">
              <span class="t-icon-swap" data-state="a"><span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span><span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span></span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </section>
</div>
