---
layout: page
title: DotCraft
description: 最适合您项目的 AI Agent，所有能力尽在工作区内。
aside: false
sidebar: false
editLink: false
lastUpdated: false
---

<div class="dc-home">
  <section class="dc-hero">
    <div class="dc-hero__inner">
      <div class="dc-hero__content">
        <p class="dc-kicker">项目原生 Agent Harness</p>
        <h1>DotCraft</h1>
        <p class="dc-hero__tagline">AI Agent 住进你的项目。所有入口，共用同一个工作区。</p>
        <p class="dc-hero__lead">
          DotCraft 把 Desktop、CLI、IDE、聊天机器人、API 与自动化任务连接到同一个项目上下文，让对话、计划、记忆和工具连续流动，而不是被困在某一个应用里。
        </p>
        <div class="dc-route-list" aria-label="DotCraft 入口">
          <span>Desktop</span>
          <span>CLI</span>
          <span>IDE</span>
          <span>Bots</span>
          <span>API</span>
          <span>Automations</span>
        </div>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
          <a class="dc-button" href="https://github.com/DotHarness/dotcraft/releases">下载 Release</a>
          <a class="dc-button" href="https://github.com/DotHarness/dotcraft">GitHub</a>
        </div>
      </div>
      <figure class="dc-hero__media">
        <img src="https://cdn.jsdelivr.net/gh/DotHarness/resources@master/dotcraft/desktop_banner.png" alt="DotCraft Desktop 预览" />
      </figure>
    </div>
  </section>

  <section id="features" class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>为什么选 DotCraft</h2>
        <p class="dc-section__text">
          它不是又一个聊天机器人，也不是一组散落的代码代理 CLI。DotCraft 把仓库当作 Agent 状态、能力与协作的长期归属。
        </p>
      </div>
      <div class="dc-grid">
        <article class="dc-card dc-card--workspace">
          <span class="dc-card__index">01</span>
          <h3>项目就是工作区</h3>
          <p>会话、记忆、技能、自动化和设置跟着项目走，所以无论从哪个入口打开，Agent 都拥有同一份上下文。</p>
        </article>
        <article class="dc-card dc-card--memory">
          <span class="dc-card__index">02</span>
          <h3>记忆是 Markdown，不是黑箱</h3>
          <p><code>MEMORY.md</code>、<code>HISTORY.md</code> 与 Dreams 把 Agent 的学习过程暴露给你审阅、修改、回滚。</p>
        </article>
        <article class="dc-card dc-card--runtime">
          <span class="dc-card__index">03</span>
          <h3>一个核心，多种入口</h3>
          <p>Desktop、TUI、IDE、群聊机器人、HTTP API 都连同一个会话核心；同一段会话可在不同设备和平台之间接力。</p>
        </article>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--quiet">
    <div class="dc-section__inner dc-showcase">
      <div>
        <p class="dc-kicker">Desktop first</p>
        <h2>图形化管理会话、Diff、计划与自动化</h2>
        <p class="dc-section__text">
          Desktop 是推荐的第一入口。下载、选工作区、配模型，5 分钟就能跑通；之后再按需打开 TUI、IDE、自动化和群聊机器人。
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">5 分钟快速开始</a>
          <a class="dc-button" href="./features/entry-points/desktop">Desktop 指南</a>
          <a class="dc-button" href="./features/entry-points/">入口总览</a>
        </div>
      </div>
      <figure class="dc-media">
        <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png" alt="DotCraft" />
      </figure>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>给开发者的协议与 SDK</h2>
        <p class="dc-section__text">
          AppServer 通过 JSON-RPC over stdio/WebSocket 投影统一会话核心，任何语言都能写客户端、做远程部署、构建机器人或自动化。
        </p>
      </div>
      <div class="dc-grid">
        <a class="dc-card dc-card--link dc-card--appserver" href="./developing/appserver">
          <span class="dc-card__index">APPSERVER</span>
          <h3>AppServer</h3>
          <p>无头服务、远程客户端、多客户端共享工作区。</p>
        </a>
        <a class="dc-card dc-card--link dc-card--editors" href="./features/entry-points/editors">
          <span class="dc-card__index">EDITORS</span>
          <h3>IDE / 编辑器</h3>
          <p>通过 ACP 接入 JetBrains、Obsidian、Unity 等编辑器。</p>
        </a>
        <a class="dc-card dc-card--link dc-card--sdks" href="./developing/sdk">
          <span class="dc-card__index">SDKs</span>
          <h3>SDK</h3>
          <p>TypeScript、.NET 与 Python SDK，用于应用、原生集成、机器人和外部渠道。</p>
        </a>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--quiet">
    <div class="dc-section__inner dc-showcase dc-showcase--reverse">
      <div>
        <p class="dc-kicker">Automations</p>
        <h2>把 Agent 工作流放进任务管线</h2>
        <p class="dc-section__text">
          本地任务、Cron、Goals 在工作区内提供调度、线程绑定、活动展示和长期目标推进能力——让 Agent 也能值班。
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./features/automations">查看 Automations</a>
          <a class="dc-button" href="./features/observability">可观测性</a>
        </div>
      </div>
      <figure class="dc-media">
        <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png" alt="DotCraft automations panel" />
      </figure>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>三步开始</h2>
        <p class="dc-section__text">第一次使用请从 Desktop 开始。跑通之后，同一工作区可以继续接入终端、编辑器、API、SDK 和自动化任务。</p>
      </div>
      <div class="dc-steps">
        <div class="dc-step"><strong>下载 Desktop</strong><span>从 Release 安装桌面应用，或从源码构建后启动 Desktop。</span></div>
        <div class="dc-step"><strong>选择项目文件夹</strong><span>选择真实项目目录，让配置、会话和任务跟随这个项目保存。</span></div>
        <div class="dc-step"><strong>配置模型并开始对话</strong><span>选择 Anthropic、OpenAI / OpenAI-compatible 或 ChatGPT OAuth 等模型 Provider，发送第一次仓库理解请求。</span></div>
      </div>
    </div>
  </section>

  <section class="dc-section dc-section--cta dc-section--final">
    <div class="dc-section__inner">
      <div class="dc-cta">
        <h2>把项目接回 Agent</h2>
        <p>从下载到第一次仓库理解请求，5 分钟跑通；同一个工作区随后能延展到你想接的任何入口。</p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
          <a class="dc-button" href="https://github.com/DotHarness/dotcraft">在 GitHub 上 Star</a>
        </div>
      </div>
    </div>
  </section>
</div>
