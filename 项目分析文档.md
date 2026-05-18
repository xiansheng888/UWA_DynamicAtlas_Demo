# 动态图集系统 - 项目分析文档

## 1. 项目概述

本项目是一个 **Unity 运行时动态图集（Dynamic Atlas）** 系统。它将运行时加载的小纹理动态打包到大图集纹理中，以减少 Draw Call 和纹理交换开销，适用于 Unity UI 系统（`UnityEngine.UI`）。

**核心价值**：多个零散的小图合并到一张大图上，UI 元素共享同一张纹理，GPU 可以批量渲染，显著提升 UI 渲染性能。

**技术栈**：Unity 2022+，C#，基于 `Resources.Load` / `Resources.LoadAsync` 加载资源。

---

## 2. 系统架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                         UI Layer (Unity)                         │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐              │
│  │UIDynamicImage│ │UIDynamicRaw  │ │UIPackingImage│  ...         │
│  │  (Image)     │ │  Image       │ │  (Image)     │              │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘              │
│         │                │                │                       │
├─────────┼────────────────┼────────────────┼───────────────────────┤
│         ▼                ▼                ▼                       │
│  ┌──────────────────────────────────────────┐                    │
│  │        DynamicAtlasManager (Singleton)    │                    │
│  │  ┌─────────────────┐ ┌─────────────────┐ │                    │
│  │  │  DynamicAtlas   │ │  PackingAtlas   │ │                    │
│  │  │  (BSP算法)       │ │  (外接矩形算法)  │ │                    │
│  │  └────────┬────────┘ └────────┬────────┘ │                    │
│  │           │                   │           │                    │
│  │  ┌────────┴───────────────────┴────────┐  │                    │
│  │  │       Object Pool (对象池)           │  │                    │
│  │  │  IntegerRectangle / SaveImageData   │  │                    │
│  │  │  GetImageData                       │  │                    │
│  │  └─────────────────────────────────────┘  │                    │
│  └──────────────────────────────────────────┘                    │
│                         │                                        │
├─────────────────────────┼────────────────────────────────────────┤
│                         ▼                                         │
│  ┌──────────────────────────────────────────┐                    │
│  │         Resource Loading Layer            │                    │
│  │  ┌──────────────┐ ┌────────────────────┐ │                    │
│  │  │ResourcesManager│ │ UnityHelpCenter   │ │                    │
│  │  │   (Proxy)     │ │ (MonoBehaviour)    │ │                    │
│  │  │               │ │ Coroutine Loader   │ │                    │
│  │  └──────────────┘ └────────────────────┘ │                    │
│  └──────────────────────────────────────────┘                    │
│                         │                                        │
├─────────────────────────┼────────────────────────────────────────┤
│                         ▼                                         │
│  ┌──────────────────────────────────────────┐                    │
│  │         Unity Resources System            │                    │
│  │         Resources/ 目录下的纹理资源         │                    │
│  └──────────────────────────────────────────┘                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 类图

```mermaid
classDiagram
    class Singleton~T~ {
        <<abstract>>
        +Instance T
    }

    class DynamicAtlasManager {
        -Dictionary~DynamicAtlasGroup, DynamicAtlas~ m_DynamicAtlas
        -Dictionary~DynamicAtlasGroup, PackingAtlas~ m_PackingAtlas
        -List~IntegerRectangle~ mRectangleStack
        -List~SaveImageData~ mSaveImageDataStack
        -List~GetImageData~ mGetImageDataStack
        +GetDynamicAtlas(group, topFirst) DynamicAtlas
        +GetPackingAtlas(group) PackingAtlas
        +AllocateRectangle(x,y,w,h) IntegerRectangle
        +ReleaseRectangle(rect)
        +AllocateSaveImageData(rect) SaveImageData
        +ReleaseSaveImageData(data)
        +AllocateGetImageData() GetImageData
        +ReleaseGetImageData(data)
        +ClearAllCache()
    }

    class DynamicAtlas {
        -List~Texture2D~ m_tex2DList
        -List~RenderTexture~ m_RenderTexList
        -List~Material~ m_MaterialList
        -List~List~IntegerRectangle~~ mFreeAreasList
        -Dictionary~string, SaveImageData~ _usingRect
        -bool mTopFirst
        +SetTexture(path, texture, callback) <<Blit模式>>
        +SetTexture(path, texture, callback) <<Copy模式>>
        +GetImage(path, callback)
        +RemoveImage(path, isClearRange)
        +Clear()
        -InsertArea(width, height, out index) IntegerRectangle
        -GetFreeArea(width, height, out index, out justRight) IntegerRectangle
        -OnMergeArea(newRect, texIndex) bool
        -GenerateDividedAreasTopFirst(index, divider, freeArea)
        -GenerateDividedAreasRightFirst(index, divider, freeArea)
    }

    class PackingAtlas {
        -List~Texture2D~ m_tex2DList
        -List~RenderTexture~ m_RenderTexList
        -List~Material~ m_MaterialList
        -List~List~IntegerRectangle~~ mFreeAreasList
        -Dictionary~string, SaveImageData~ _usingRect
        -List~Vector2~ m_FindRange
        -IntegerRectangle mOutsideRectangle
        +SetTexture(path, texture, callback)
        +ClearAtlas()
        -InsertArea(width, height, out index) IntegerRectangle
        -GetFreeArea(width, height, out index, out justRight) IntegerRectangle
        -generateNewFreeAreas(index, divider)
        -filterSelfSubAreas(index)
    }

    class IntegerRectangle {
        +int x, y, width, height, id
        +int right
        +int top
        +int size
        +Rect rect
    }

    class SaveImageData {
        +int texIndex
        +int referenceCount
        +Rect rect
        +IntegerRectangle rectangle
    }

    class GetImageData {
        +string path
        +OnCallBackTexRect callback
        +OnCallBackMetRect BlitCallback
    }

    class DynamicAtlasGroup {
        <<enum>>
        Size_256 = 256
        Size_512 = 512
        Size_1024 = 1024
        Size_2048 = 2048
    }

    class AtlasConfig {
        <<static>>
        +bool kUsingCopyTexture
        +TextureFormat kTextureFormat
        +RenderTextureFormat kRenderTextureFormat
    }

    class UIDynamicImage {
        -DynamicAtlas m_Atlas
        -string _currentPath
        +SetImage(path, callback)
        +SetImageNoHide(path, callback)
        +OnDispose()
    }

    class UIDynamicRawImage {
        -DynamicAtlas m_Atlas
        -string _currentPath
        +SetImage(path, callback)
        +SetImageNoHide(path, callback)
        +OnDispose()
    }

    class UIPackingImage {
        -PackingAtlas m_Atlas
        +SetGroup(group)
        +OnDispose()
    }

    class UIPackingRawImage {
        -PackingAtlas m_Atlas
        +SetImage(path, texture, callback)
        +OnDispose()
    }

    class ResourcesManager {
        +Instance ResourcesManager
        +LoadResSync(path, type) Object
        +LoadResAsync(path, callback, type)
    }

    class UnityHelpCenter {
        -List~ResCallBackData~ loadResTasks
        +LoadResSync(path, type) Object
        +LoadResourceAsync(path, callback, type)
        -StartLoadResourceAsync()
    }

    Singleton~T~ <|-- DynamicAtlasManager
    DynamicAtlasManager --> DynamicAtlas : manages
    DynamicAtlasManager --> PackingAtlas : manages
    DynamicAtlasManager --> IntegerRectangle : pools
    DynamicAtlasManager --> SaveImageData : pools
    DynamicAtlasManager --> GetImageData : pools
    DynamicAtlas --> IntegerRectangle : uses
    DynamicAtlas --> SaveImageData : uses
    DynamicAtlas --> GetImageData : uses
    DynamicAtlas ..> DynamicAtlasGroup : configured by
    PackingAtlas --> IntegerRectangle : uses
    PackingAtlas --> SaveImageData : uses
    PackingAtlas ..> DynamicAtlasGroup : configured by
    UIDynamicImage --> DynamicAtlas : uses
    UIDynamicImage ..> DynamicAtlasGroup : configured by
    UIDynamicRawImage --> DynamicAtlas : uses
    UIPackingImage --> PackingAtlas : uses
    UIPackingRawImage --> PackingAtlas : uses
    ResourcesManager --> UnityHelpCenter : delegates to
    DynamicAtlas ..> ResourcesManager : async loads via
    PackingAtlas ..> ResourcesManager : async loads via
    DynamicAtlas ..> AtlasConfig : reads
    PackingAtlas ..> AtlasConfig : reads
```

---

## 4. 核心数据结构

```
IntegerRectangle (矩形)
┌─────────────────────────────┐
│ x, y        坐标原点          │
│ width, height  宽高          │
├─────────────────────────────┤
│ right  = x + width          │  ← 计算属性
│ top    = y + height         │  ← 计算属性
│ size   = width * height     │  ← 计算属性
│ rect   → UnityEngine.Rect  │  ← 转换属性
└─────────────────────────────┘

SaveImageData (已使用区域记录)
┌──────────────────────────────────┐
│ texIndex: int        所在图集索引  │
│ referenceCount: int  引用计数      │
│ rect: Rect           UV矩形       │
│ rectangle: IntegerRectangle 带padding的原始矩形 │
└──────────────────────────────────┘

DynamicAtlasGroup (图集尺寸枚举)
┌────────────┬──────┐
│ Size_256   │ 256  │
│ Size_512   │ 512  │
│ Size_1024  │ 1024 │
│ Size_2048  │ 2048 │
└────────────┴──────┘
```

---

## 5. 两种图集算法对比

| 特性 | DynamicAtlas | PackingAtlas |
|------|-------------|-------------|
| **切割策略** | BSP（二分空间分割） | 外接矩形启发式 |
| **切割方向** | TopFirst / RightFirst 可选 | 四个方向均分割 |
| **空闲区域管理** | 单矩形区域链表 | 多矩形自由区域 + 过滤子区域 |
| **查找加速** | 无 | `m_FindRange` 记录已用范围 |
| **释放合并** | 支持（四方向邻接合并） | 不支持合并 |
| **Padding处理** | 固定3px | 固定8px |
| **Blit模式** | Graphics.Blit | GL.Blit（GL直接绘制） |

**DynamicAtlas 切割示意图（TopFirst）：**

```
Before (freeArea)              After
┌──────────────────┐    ┌──────┬───────────┐
│                  │    │      │ 空闲区域2  │
│   空闲区域        │    │placed│ (右侧)    │
│                  │ -> │      │           │
│                  │    ├──────┴───────────┤
│                  │    │    空闲区域1      │
└──────────────────┘    │    (上方)        │
                        └──────────────────┘
```

**PackingAtlas 切割示意图（四方向）：**

```
         空闲区域1 (上)
         ┌──────────┐
 空闲区域2│ placed  │空闲区域3
 (左)    │         │ (右)
         ├──────────┤
         │ 空闲区域4 │ (下)
         └──────────┘
```

---

## 6. 核心流程图

### 6.1 图片加载与放置流程

```mermaid
flowchart TD
    A[UI组件调用 SetImage/GetImage] --> B{_usingRect<br/>已包含此路径?}
    B -->|是| C[引用计数+1<br/>直接返回已有的纹理/材质+UV]
    B -->|否| D[创建 GetImageData<br/>加入 mGetImageTasks 队列]
    D --> E{队列长度 == 1?}
    E -->|是| F[调用 OnGetImage<br/>或 OnRenderTexture]
    E -->|否| G[等待前面的任务完成<br/>（队列化处理）]
    F --> H[ResourcesManager<br/>异步加载纹理]
    H --> I[OnRenderTexture 回调]
    I --> J{纹理为空?}
    J -->|是| K[遍历任务队列<br/>回调 null + 空Rect]
    J -->|否| L[调用 InsertArea<br/>在空闲区域中查找合适位置]
    L --> M{找到合适<br/>空闲区域?}
    M -->|找到| N[根据图集索引<br/>CopyTexture 或 BlitTexture]
    M -->|未找到| O[CreateNewAtlas<br/>创建新图集]
    O --> N
    N --> P[创建 SaveImageData<br/>记录纹理在图集中的位置]
    P --> Q[遍历mGetImageTasks<br/>回调节点匹配路径的任务]
    Q --> R[从任务队列移除<br/>回收 GetImageData 到对象池]
```

### 6.2 DynamicAtlas 空闲区域查找流程

```mermaid
flowchart TD
    A[GetFreeArea] --> B[遍历所有图集的 mFreeAreasList]
    B --> C{当前图集有<br/>能容纳图片的区域?}
    C -->|是| D{宽/高恰好<br/>等于图集宽/高?}
    D -->|是| E[记录为 perfectlyFit<br/>继续查看是否有精确匹配]
    E --> F{精确匹配?<br/>宽=area宽 且 高=area高}
    F -->|是| G[直接返回此区域]
    F -->|否| H{tempArea为空<br/>或新区域更优?}
    H -->|TopFirst: 高度更小<br/>RightFirst: 宽度更小| I[更新 tempArea]
    H -->|否| J[保持 tempArea]
    I --> K[继续遍历当前图集]
    J --> K
    C -->|否| L{已找到过<br/>可用区域?}
    L -->|是| M[跳出循环<br/>返回tempArea]
    L -->|否| N[继续下一个图集]
    K --> C
    N --> B
    M --> O[返回区域 + 索引]
    G --> O
    B -->|所有图集遍历完<br/>仍未找到| P[CreateNewAtlas<br/>创建新图集]
    P --> Q[返回新图集的<br/>第一个空闲区域]
```

### 6.3 纹理释放与区域合并流程（仅 DynamicAtlas）

```mermaid
flowchart TD
    A[RemoveImage path] --> B{_usingRect<br/>包含此路径?}
    B -->|否| C[直接返回]
    B -->|是| D[referenceCount--]
    D --> E{referenceCount == 0?}
    E -->|否| F[仅减计数，不释放]
    E -->|是| G[isClearRange?<br/>清除图集上对应区域像素]
    G --> H[调用 OnMergeArea<br/>开始合并流程]
    H --> I[OnMergeAreaRecursive<br/>四方向检测邻接空闲区域]
    I --> J{右邻接?<br/>target.right==free.x<br/>且y,height相同}
    J -->|是| K[合并为更大矩形]
    J -->|否| L{上邻接?<br/>target.top==free.y<br/>且x,width相同}
    L -->|是| K
    L -->|否| M{左邻接?<br/>target.x==free.right<br/>且y,height相同}
    M -->|是| K
    M -->|否| N{下邻接?<br/>target.y==free.top<br/>且x,width相同}
    N -->|是| K
    N -->|否| O[无法继续合并<br/>将最终矩形加入空闲列表]
    K --> P[移除被合并的空闲区域]
    P --> I
    O --> Q[从_usingRect中移除<br/>回收SaveImageData]
```

### 6.4 PackingAtlas 空闲区域生成流程

```mermaid
flowchart TD
    A[generateNewFreeAreas] --> B[遍历当前图集<br/>所有空闲区域]
    B --> C{divider与当前<br/>空闲区域有重叠?}
    C -->|否| D[保留此空闲区域]
    C -->|是| E[generateDividedAreas<br/>四方向切割]
    E --> F1[右剩余: area.right - divider.right]
    F1 --> G1{宽度 > 0?}
    G1 -->|是| H1[创建右侧剩余区域]
    G1 -->|否| F2[左剩余: divider.x - area.x]
    F2 --> G2{宽度 > 0?}
    G2 -->|是| H2[创建左侧剩余区域]
    G2 -->|否| F3[下剩余: area.top - divider.top]
    F3 --> G3{高度 > 0?}
    G3 -->|是| H3[创建下方剩余区域]
    G3 -->|否| F4[上剩余: divider.y - area.y]
    F4 --> G4{高度 > 0?}
    G4 -->|是| H4[创建上方剩余区域]
    G4 -->|否| I[从空闲列表移除原始区域<br/>回收IntegerRectangle]
    H1 --> J[将新区域添加到 mNewFreeAreas]
    H2 --> J
    H3 --> J
    H4 --> J
    J --> I
    I --> K[filterSelfSubAreas<br/>过滤被包含的子区域]
    K --> L[更新 mFindRange<br/>记录打包边界]
```

---

## 7. 数据流转图

```
                        ┌──────────────────────────────┐
                        │       Resources/ 目录          │
                        │  test.png, BUFF/, NPC/, ...   │
                        └──────────────┬───────────────┘
                                       │
                              Resources.LoadAsync
                                       │
                                       ▼
┌──────────────────────────────────────────────────────────────────┐
│                     UnityHelpCenter                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ loadResTasks: List<ResCallBackData>                          │ │
│  │ ┌──────────┬──────────┬──────────┐                          │ │
│  │ │ path:"a" │ path:"b" │ path:"c" │  ← 合并同路径请求          │ │
│  │ │ cb: cb1  │ cb: cb2  │ cb: cb3  │                          │ │
│  │ └──────────┴──────────┴──────────┘                          │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬───────────────────────────────────┘
                               │ 回调: (path, Texture2D)
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                     DynamicAtlas / PackingAtlas                    │
│                                                                    │
│  输入: path + Texture2D                                           │
│                                                                    │
│  ┌─────────────────┐    ┌──────────────────┐                      │
│  │ 空闲区域查找      │───▶│ 区域切割/标记占用  │                      │
│  │ (GetFreeArea)   │    │ (InsertArea)     │                      │
│  └─────────────────┘    └────────┬─────────┘                      │
│                                  │                                 │
│                                  ▼                                 │
│  ┌─────────────────────────────────────────────┐                  │
│  │          纹理数据写入图集                      │                  │
│  │  CopyTexture: Graphics.CopyTexture()         │                  │
│  │  Blit:       Graphics.Blit() + CustomShader  │                  │
│  └──────────────────────┬──────────────────────┘                  │
│                         │                                          │
│                         ▼                                          │
│  ┌─────────────────────────────────────────────┐                  │
│  │  _usingRect[path] = SaveImageData            │                  │
│  │    ├─ texIndex: 图集索引                      │                  │
│  │    ├─ rect: UV矩形 (归一化坐标)                │                  │
│  │    ├─ rectangle: 原始矩形 (像素坐标+padding)   │                  │
│  │    └─ referenceCount: 引用计数                │                  │
│  └──────────────────────┬──────────────────────┘                  │
│                         │                                          │
│  输出: callback(Material/Texture2D, Rect uv, string path)         │
└──────────────────────────────┬───────────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────────┐
│                     UI Components                                  │
│                                                                    │
│  UIDynamicImage:                                                   │
│    纹理 → sprite = Sprite.Create(tex2D, spriteRect, ...)          │
│    UV  → 内部计算（Sprite自动处理）                                 │
│                                                                    │
│  UIDynamicRawImage:                                                │
│    纹理 → this.texture = tex2D  (or this.material = material)     │
│    UV  → uvRect = rect                                            │
│                                                                    │
│  隐藏/显示:                                                        │
│    SetImage → gameObject.SetActiveVirtual(false)                  │
│    callback → gameObject.SetActiveVirtual(true)                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## 8. 两种渲染路径

```
                    AtlasConfig.kUsingCopyTexture
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
     true (CopyTexture)              false (Blit)
     ─────────────────               ─────────────
     
     输出: Texture2D                  输出: RenderTexture + Material
     
     写入方式:                        写入方式:
     Graphics.CopyTexture            Graphics.Blit / GL.Blit
     (src, 0,0,0,0,                 (srcTex, destRT,
      w,h, dstTex,                   customMaterial, 0)
      0,0, posX, posY)
                                     依赖 Shader:
     优点: 简单直接                  "DynamicAtlas/GraphicBlit"
     缺点: 固定内存占用              通过 _DrawRect 参数
           每个图集占用显存            裁剪并重映射UV
     
     适用UI:                         适用UI:
     UIDynamicImage ✓               UIDynamicRawImage ✓
     UIDynamicRawImage ✓

     图集存储:
     ┌─────────────────┐
     │ Texture2D[]      │              ┌─────────────────┐
     │ (m_tex2DList)    │              │ RenderTexture[] │
     │                  │              │ (m_RenderTexList)│
     │ Material:        │              │                  │
     │ UI/Default       │              │ Material:        │
     │ .mainTexture     │              │ UI/Default       │
     └─────────────────┘              │ .mainTexture = RT│
                                      └─────────────────┘
```

---

## 9. 对象池机制

`DynamicAtlasManager` 维护三个对象池栈，通过 `Pop()` 复用已分配对象，避免频繁 GC：

```
┌─────────────────────────────────────────────────────────────────┐
│                     DynamicAtlasManager                          │
│                                                                  │
│  mRectangleStack       mSaveImageDataStack    mGetImageDataStack │
│  ┌────┐ ┌────┐ ┌────┐  ┌────┐ ┌────┐ ┌────┐  ┌────┐ ┌────┐      │
│  │ IR │ │ IR │ │ IR │  │SID │ │SID │ │SID │  │GID │ │GID │      │
│  │    │ │    │ │    │  │    │ │    │ │    │  │    │ │    │      │
│  └────┘ └────┘ └────┘  └────┘ └────┘ └────┘  └────┘ └────┘      │
│                                                                  │
│  Allocate*(...) ──────► Pop() ──► 复用或 new                     │
│  Release*(obj)  ──────► Push(obj) ──► 回收到栈                    │
└─────────────────────────────────────────────────────────────────┘

IntegerRectangle  ~200B/个  (频繁分配/回收，每次插入/合并)
SaveImageData     ~48B/个   (每个已放置的纹理一条)
GetImageData      ~64B/个   (每次异步请求一条)
```

---

## 10. 文件组织结构

```
Assets/
├── DynamicAtlas/
│   ├── AtlasConfig.cs            # 全局配置（纹理格式、渲染路径）
│   ├── DynamicAtlas.cs           # BSP图集引擎
│   ├── DynamicAtlasData.cs       # 数据类（枚举、矩形、请求/存储数据）
│   ├── DynamicAtlasManager.cs    # 图集管理器（单例 + 对象池）
│   ├── DynamicAtlasDemo.cs       # 演示脚本
│   ├── UIDynamicImage.cs         # 图集Image组件
│   ├── UIDynamicRawImage.cs      # 图集RawImage组件
│   ├── Editor/
│   │   ├── DynamicAtlasWindow.cs # 运行时图集可视化窗口
│   │   ├── PackingAtlasWindow.cs # PackingAtlas可视化窗口
│   │   ├── UIDynamicImageEditor.cs
│   │   ├── UIDynamicRawImageEditor.cs
│   │   ├── UIPackingImageEditor.cs
│   │   └── UIPackingRawImageEditor.cs
│   └── PackingAtlas/
│       ├── PackingAtlas.cs       # 外接矩形图集引擎
│       ├── PackingAtlasDemo.cs   # 演示脚本
│       ├── UIPackingImage.cs     # Packing Image组件
│       └── UIPackingRawImage.cs  # Packing RawImage组件
├── Other/
│   ├── AnoUtil.cs               # List<T>.Pop() 扩展
│   ├── CommonUtils.cs           # 工具函数
│   ├── PathUtil.cs              # 路径转换工具
│   ├── ResourcesManager.cs      # 资源加载代理
│   ├── Singleton.cs             # 泛型单例基类
│   ├── UIComponentMenuItemEditor.cs # 编辑器菜单项
│   ├── UITools.cs               # UI工具扩展
│   ├── UnityForDelegateBase.cs  # 委托类型定义
│   └── UnityHelpCenter.cs       # 协程资源加载器
└── Resources/
    ├── DynamicAtlasGraphicBlitShader.shader  # 自定义Blit着色器
    ├── BUFF/                     # 示例资源
    ├── NPC/                      # 示例资源
    ├── skill/                    # 示例资源
    └── test.png                  # 示例资源
```

---

## 11. 关键设计决策

| 决策 | 说明 |
|------|------|
| **两种图集引擎并存** | DynamicAtlas 适合顺序加载场景（BSP+合并），PackingAtlas 适合批量打包场景（更优的空间利用率） |
| **引用计数** | 同一路径可被多个UI组件复用，无需重复加载和存储 |
| **对象池** | IntegerRectangle 频繁创建/销毁，对象池显著减少 GC Alloc |
| **异步队列化** | GetImage 批量请求同一路径时，只发起一次异步加载，结果分发给所有等待者 |
| **静态配置类** | AtlasConfig 使用静态字段而非 ScriptableObject，简单但有平台切换成本 |
| **两套回调系统** | CopyTexture 路径回传 Texture2D，Blit 路径回传 Material，UI组件据此适配 |

---

## 12. 潜在问题与改进方向

1. **PackingAtlas 不支持释放合并**：纹理移除后空闲区域碎片化，长期运行会浪费空间
2. **AtlasConfig 不可热切换**：`kUsingCopyTexture` 是静态字段，运行时切换会导致新旧路径不一致
3. **无最大图集数限制**：理论上可无限创建新图集，缺乏内存上限保护
4. **PackingAtlas 的 RemoveImage 未实现**：UIPackingImage/UIPackingRawImage 的 OnDispose 中 RemoveImage 被注释掉
5. **RendererTexture.Blit 路径未完全验证**：UIDynamicImage 明确禁用了 Blit 路径（注释写"比较麻烦，就没有实现"）
