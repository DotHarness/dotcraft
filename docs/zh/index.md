---
layout: page
title: DotCraft
description: 住进项目里的 AI Agent——而不是某一个应用里。
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
        <p class="dc-hero__eyebrow t-stagger-line t-stagger-line--1">项目级 Agent 运行时</p>
        <h1 class="t-stagger-line t-stagger-line--2">让 AI Agent 住进<em>项目</em>里。</h1>
        <p class="dc-hero__contrast t-stagger-line t-stagger-line--3">而不是某一个应用里。</p>
        <p class="dc-hero__lead t-stagger-line t-stagger-line--4">
          Desktop、CLI、IDE、社交渠道和你自己的应用共享同一个工作区，项目的上下文随你同行。
        </p>
        <div class="dc-hero__cta t-stagger-line t-stagger-line--5">
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
          <p class="dc-hero__meta">开源 · 可自托管 · Apache-2.0</p>
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
          <p>
            对话、记忆、Agent、技能、插件和自动化都留在项目里。无论从 Desktop、CLI、社交渠道还是接入的应用打开，都能在同一份上下文中继续。
          </p>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/project-workspace">项目工作区 <span aria-hidden="true">→</span></a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/multi-workspace.gif" alt="在 DotCraft Desktop 中切换项目工作区" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">Agent 与团队</p>
          <h2>一个 Agent 独当一面，或一支团队协同作战。</h2>
          <p>
            Agent Profile 让 Agent 可复用——指令、模型、工具与权限一次定义；Agent Teams 把复杂工作拆给各有所长的成员，Goals 让长期任务持续推进。
          </p>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/agent-system/agent-profiles">Agent Profiles <span aria-hidden="true">→</span></a>
            <a class="dc-link" href="./features/agent-system/teams">Teams <span aria-hidden="true">→</span></a>
            <a class="dc-link" href="./features/agent-system/automations">自动化 <span aria-hidden="true">→</span></a>
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
          <p>
            通过 API、App Binding，或 TypeScript、.NET、Python SDK 接入。应用保留自己的数据与流程，DotCraft 负责对话、工具、审批、记忆和追踪。
          </p>
          <div class="dc-story__links">
            <a class="dc-link" href="./developing/integrations/app-binding">App Binding <span aria-hidden="true">→</span></a>
            <a class="dc-link" href="./developing/sdks/">SDK <span aria-hidden="true">→</span></a>
          </div>
        </div>
        <figure class="dc-story__media">
          <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/whats-new/desktop-extensions.gif" alt="Oratorio 项目看板作为完整视图嵌入 DotCraft Desktop" loading="lazy" />
        </figure>
      </div>
    </article>
    <article class="dc-story dc-story--flip">
      <div class="dc-story__inner dc-reveal">
        <div class="dc-story__copy">
          <p class="dc-story__eyebrow">离开桌面之后</p>
          <h2>窗口关上，工作仍在继续。</h2>
          <p>
            自动化按计划运行，后台渠道保持连接，Channel Handoff 让正在进行的对话无缝转到 Telegram、微信、飞书等渠道——不必从头再来。
          </p>
          <div class="dc-story__links">
            <a class="dc-link" href="./features/entry-points/channels">渠道与机器人 <span aria-hidden="true">→</span></a>
            <a class="dc-link" href="./features/agent-system/automations">定时任务 <span aria-hidden="true">→</span></a>
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
      <p class="dc-thesis__note">
        多数 AI 编程工具都在优化一位开发者与一个客户端之间的循环。DotCraft 关注更底下的那一层：一个属于项目、被所有客户端与应用共享的持久运行时。
      </p>
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

  <section class="dc-start">
    <div class="dc-start__inner dc-reveal">
      <div class="dc-section__header">
        <h2>五分钟开始</h2>
        <p class="dc-section__text">
          打开一个真实的项目文件夹，接入一个模型提供商，然后提一个具体的需求。
        </p>
      </div>
      <div class="dc-start__grid">
        <div class="dc-start__col">
          <h3>Desktop</h3>
          <p>下载、选择工作区、配置模型——推荐的第一入口。</p>
          <div class="dc-actions">
            <a class="dc-button dc-button--primary" href="https://github.com/DotHarness/dotcraft/releases">下载 Desktop</a>
            <a class="dc-button" href="./getting-started">快速开始</a>
          </div>
        </div>
        <div class="dc-start__col">
          <h3>CLI</h3>
          <p>一行命令安装；<code>dotcraft exec</code> 直接运行一次性项目任务。</p>
          <div class="dc-start__cmds">
            <div class="dc-cmd">
              <div class="dc-cmd__head">
                <span>macOS / Linux</span>
                <button class="dc-cmd__copy" type="button" data-copy aria-label="复制安装命令">
                  <span class="t-icon-swap" data-state="a">
                    <span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span>
                    <span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span>
                  </span>
                </button>
              </div>
              <code>curl -fsSL https://www.dotcraft.net/install.sh | bash</code>
            </div>
            <div class="dc-cmd">
              <div class="dc-cmd__head">
                <span>Windows</span>
                <button class="dc-cmd__copy" type="button" data-copy aria-label="复制安装命令">
                  <span class="t-icon-swap" data-state="a">
                    <span class="t-icon" data-icon="a"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg></span>
                    <span class="t-icon" data-icon="b"><svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg></span>
                  </span>
                </button>
              </div>
              <code>irm https://www.dotcraft.net/install.ps1 | iex</code>
            </div>
          </div>
        </div>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--cta dc-section--final">
    <div class="dc-cta dc-reveal">
      <h2>准备好把项目接回家了吗？</h2>
      <p>从下载到第一个理解你仓库的会话只要五分钟——之后加入的每个入口，都通向同一个工作区。</p>
      <div class="dc-actions">
        <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
        <a class="dc-button" href="https://github.com/DotHarness/dotcraft">GitHub 加星</a>
      </div>
    </div>
  </section>
</div>
