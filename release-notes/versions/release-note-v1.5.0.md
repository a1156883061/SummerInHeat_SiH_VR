# v1.5.0 Release Notes

## English

### OpenXR Physical Hand Interaction

- Added runtime OpenXR hand collider proxies for DynamicBone-driven body, hair, and clothing physics.
- Added runtime `MagicaCloth2.MagicaSphereCollider` proxies at the OpenXR controller grip poses.
- Added MagicaCloth2 registration through both serialized `colliderList` patching and runtime `ColliderManager.AddCollider(...)` when available.

### Physics Diagnostics

- Added optional OpenXR physics diagnostics behind the `PHYSICS_LOG` compile flag.
- Added logs for nearby colliders, focused physics fields, root physics inventory, and full component dumps.
- Removed diagnostic list truncation so development builds can inspect every matching physics component.
- Added build support for `PHYSICS_LOG` through `/p:PhysicsLog=true` and `build.ps1 -PhysicsLog`.

### Logging

- Added configurable mod log filtering.
- Changed the default log level to `Warning` to reduce noisy runtime input and diagnostic logs.
- `Off` now disables all Unity VR Mod log output after configuration initialization.

### Configuration

| Config | Type | Default | Description |
| --- | --- | --- | --- |
| `OpenXR Enable DynamicBone Hand Colliders` | Boolean | `true` | Enables OpenXR controller collider proxies for DynamicBone physics. |
| `OpenXR DynamicBone Hand Collider Radius` | Float | `0.06` | Radius in meters for DynamicBone hand collider proxies. |
| `OpenXR Enable MagicaCloth Hand Colliders` | Boolean | `true` | Enables OpenXR controller `MagicaCloth2.MagicaSphereCollider` proxies for MagicaCloth2 physics. |
| `OpenXR MagicaCloth Hand Collider Radius` | Float | `0.06` | Radius in meters for MagicaCloth2 hand collider proxies. |
| `OpenXR Enable Physics Diagnostics` | Boolean | `false` | Enables OpenXR physics diagnostic logs. Only available in `PHYSICS_LOG` builds. |
| `OpenXR Physics Diagnostics Radius` | Float | `0.08` | Radius in meters for the diagnostic overlap probe around each controller. Only available in `PHYSICS_LOG` builds. |
| `OpenXR Physics Diagnostics Interval Seconds` | Float | `1.0` | Minimum seconds between physics diagnostic log entries. Only available in `PHYSICS_LOG` builds. |
| `Log Level` | Enum | `Warning` | Controls Unity VR Mod log output. Values: `Off`, `Error`, `Warning`, `Info`. |

### Notes

- Existing generated config files keep their current values. If the new hand collider options already exist in a config file, set them to `true` there to enable them.
- The DynamicBone and MagicaCloth hand collision features are experimental and depend on the target character or clothing physics setup.
- Set `Log Level` to `Info` when verifying patch counts such as DynamicBone or MagicaCloth collider injection logs.

## 中文

### OpenXR 手部物理交互

- 新增 OpenXR 手柄位置的 DynamicBone 碰撞代理，用于身体、头发、服装等 DynamicBone 物理。
- 新增 OpenXR 手柄 grip pose 上的 `MagicaCloth2.MagicaSphereCollider` 碰撞代理。
- MagicaCloth2 同时尝试写入序列化 `colliderList`，并在运行时可用时调用 `ColliderManager.AddCollider(...)` 注册碰撞器。

### 物理诊断

- 新增由 `PHYSICS_LOG` 条件编译控制的 OpenXR 物理诊断功能。
- 新增附近碰撞体、重点物理字段、根对象物理组件清单、全部组件 dump 等诊断日志。
- 移除诊断列表数量限制，开发调试构建可以完整查看匹配到的物理组件。
- 支持通过 `/p:PhysicsLog=true` 和 `build.ps1 -PhysicsLog` 开启 `PHYSICS_LOG` 构建。

### 日志

- 新增可配置的 Mod 日志过滤。
- 默认日志等级调整为 `Warning`，减少运行时输入和诊断日志刷屏。
- `Off` 会在配置初始化后关闭所有 Unity VR Mod 日志输出。

### 配置项

| 配置项 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `OpenXR Enable DynamicBone Hand Colliders` | 布尔 | `true` | 是否启用 OpenXR 手柄位置的 DynamicBone 碰撞代理。 |
| `OpenXR DynamicBone Hand Collider Radius` | 浮点数 | `0.06` | DynamicBone 手部碰撞代理半径，单位为米。 |
| `OpenXR Enable MagicaCloth Hand Colliders` | 布尔 | `true` | 是否启用 OpenXR 手柄位置的 `MagicaCloth2.MagicaSphereCollider` 碰撞代理。 |
| `OpenXR MagicaCloth Hand Collider Radius` | 浮点数 | `0.06` | MagicaCloth2 手部碰撞代理半径，单位为米。 |
| `OpenXR Enable Physics Diagnostics` | 布尔 | `false` | 是否启用 OpenXR 物理诊断日志。仅在 `PHYSICS_LOG` 构建中可用。 |
| `OpenXR Physics Diagnostics Radius` | 浮点数 | `0.08` | 控制器周围物理诊断 overlap 探测半径，单位为米。仅在 `PHYSICS_LOG` 构建中可用。 |
| `OpenXR Physics Diagnostics Interval Seconds` | 浮点数 | `1.0` | 物理诊断日志最小输出间隔，单位为秒。仅在 `PHYSICS_LOG` 构建中可用。 |
| `Log Level` | 枚举 | `Warning` | 控制 Unity VR Mod 日志输出。可选值：`Off`、`Error`、`Warning`、`Info`。 |

### 说明

- 已生成的配置文件会保留现有值。如果新手部碰撞配置项已经存在于 cfg 中，需要在 cfg 里手动设为 `true`。
- DynamicBone 和 MagicaCloth 手部碰撞仍属于实验功能，实际效果取决于角色或服装使用的物理系统。
- 验证 DynamicBone 或 MagicaCloth 注入数量时，请把 `Log Level` 设为 `Info`。
