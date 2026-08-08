# Connected Apps

Connected Apps 让会话可以使用你日常产品中的工具。

![先为工作区连接 App，再为每个会话选择是否使用](/connected-apps-flow.svg)

先把 App 连接到当前工作区，再选择哪些会话可以使用它。

## 连接 App

1. 打开 **Plugins / 插件**，然后打开提供该 App 的插件。
2. 点击 **Install / 安装**。
3. 检查确认信息，然后点击 **Add to DotCraft / 添加到 DotCraft**。
4. 如果配置流程要求安装配套应用，请点击 **Install app / 安装应用**，完成安装后再点击 **Refresh / 刷新**。
5. 点击 **Connect / 连接**。
6. 在 App 中完成确认，然后返回 DotCraft。

配置完成后，插件会在 **App Settings / 应用设置** 中显示这个 App。需要重新连接或断开时，从这里进入。

## 在新会话中使用 App

1. 打开一个新会话。
2. 点击 **Apps / 应用**。
3. 启用这个会话需要使用的 Apps。
4. 发送第一条消息。

DotCraft 会先准备好选中的 Apps，再发送第一条消息。

> [!NOTE]
> 新会话可能已经选中已连接的 Apps。发送第一条消息前，请检查列表并关闭不需要的 App。

## 调整已有会话中的 Apps

打开会话标题栏中的 **Apps / 应用**，然后启用或关闭 App。

这个操作只影响当前会话，不会断开工作区连接，也不会影响其他会话。

## 审查新增访问能力

App 增加新能力时，旁边会出现 **Review / 审查**。

- 选择 **Keep previous capabilities / 保留原有能力**，拒绝新增访问并保留之前批准的范围。在 App 按原范围重新连接前，它可能暂时不可用。
- 选择 **Accept capabilities / 接受新能力**，允许当前会话使用扩展后的能力。

接受前先检查变化，尤其是新增写入操作或更大数据访问范围时。

## 重新连接或断开 App

打开插件，然后进入 **App Settings / 应用设置**。

- 连接过期或 App 要求重新登录时，点击 **Reconnect / 重新连接**。
- 打开 **Connected / 已连接**，然后点击 **Disconnect / 断开连接**，移除工作区连接。

> [!CAUTION]
> 关闭 App 只会将它从一个会话中移除；在 **App Settings / 应用设置** 中断开连接会影响当前工作区的所有会话。

通过社交渠道使用 App 时，请按对应渠道的绑定流程操作。详见 [Channels 与 Bots](../entry-points/channels)。

## 相关文档

- [插件与工具](./plugins-tools)
- [插件市场](./plugin-marketplaces)
- [Channels 与 Bots](../entry-points/channels)
- [安全与沙箱](../self-hosted/security)
- [DotCraft App](../../developing/integrations/app-binding)
