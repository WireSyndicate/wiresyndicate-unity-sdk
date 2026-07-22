# WireSyndicate Unity SDK (v1.0.0-ALPHA)

Welcome to the **WireSyndicate Unity SDK**. This SDK allows supply-side game developers to effortlessly integrate dynamic, native in-game advertising into their Unity 3D environments, fully backed by our Zero-Trust Edge architecture.

## 🔒 Zero-Trust Architecture & Mathematical Guarantees

WireSyndicate operates on a strict **Zero-Trust** model. We do not trust the client. 
To guarantee mathematical precision for demand-side advertisers and prevent spoofing, this SDK implements an **Ephemeral Token Handshake**:
- Upon initialization, the SDK executes a secure cryptographic handshake with our Edge Network using your `network_key`.
- The Edge issues an ephemeral session token and a cryptographically derived HMAC-SHA256 secret.
- All telemetry and financial clearing events are uniquely signed by this secret, ensuring that every ad impression is mathematically proven and cannot be spoofed by script kiddies or replay attacks.

### The Black-Box Protocol (Gaze Verification)
Developers **do not** write custom logic to calculate viewability. WireSyndicate uses a dynamic, algorithmic gaze-tracking system:
- **500ms Floor:** An asset must be mathematically visible on-screen for at least 500 uninterrupted milliseconds.
- **Occlusion & Frustum Culling:** The SDK natively calculates screen coverage, frustum boundaries, and ray-casted occlusion. If a player looks away, or the ad is blocked by geometry, the timer resets.
- Once the algorithm mathematically verifies the impression, it burns the signed token and dispatches the telemetry.

---

## 📦 Installation

This SDK is distributed securely via the Unity Package Manager (UPM).

1. Open your Unity project (Unity 2022.3+ recommended).
2. Navigate to **Window > Package Manager**.
3. Click the **+** icon in the top left corner and select **Add package from git URL...**
4. Enter the following URL:
   ```
   https://github.com/WireSyndicate/wiresyndicate-unity-sdk.git
   ```
5. Click **Add**. Unity will automatically download and mount the immutable SDK into your project.

---

## 🚀 Initialization

Before any assets can be fetched or telemetry dispatched, you must establish the cryptographic perimeter.

1. In your initial loading scene or main menu, create an empty GameObject and name it `[WireSyndicate]`.
2. Click **Add Component**, search for `WireSyndicateInitializer`, and attach it. Due to its `[DisallowMultipleComponent]` architecture, you can only attach one instance per GameObject.
3. In the Inspector, locate the **Network Key** field. Paste your **Organization ID** or **Game Network Key** into the field.
4. Locate the **API Base URL** field. It defaults to `https://api.wiresyndicate.com` for production. If you are doing local testing against your own environment, you can change this to `http://localhost:3000`.

> **Architecture Note:** The `WireSyndicateInitializer` is a strict `DontDestroyOnLoad` Singleton. It features built-in auto-bootstrapping, meaning it will automatically instantiate internal dependencies (like the Gaze Verification Engine). You do not need to attach them manually.

*When the scene plays, the Initializer will automatically trigger the Ephemeral Token Handshake via `GET` request and lock the session. If the key is invalid or your account is suspended, the SDK will gracefully intercept the 403/404 response and halt telemetry safely.*

---

## 🎯 Creating Placements (Advanced Configuration)

Once the SDK is initialized, you can map physical ad spaces in your 3D world. The zero-trust architecture requires a strict separation of concerns between spatial calculation and visual rendering. 

### 1. The Master Anchor: `WSPlacementNode`
The `WSPlacementNode` acts as the spatial "brain" of the ad space. It feeds precise bounding box coordinates to the Gaze Verification Engine to calculate occlusion and viewability. 

- **Placement ID:** You must retrieve this unique UUID from the WireSyndicate Network Console and paste it into the Inspector.
- **Target Renderer (LOD Architecture):** By default, the node will attempt to find a renderer on itself. However, in modern AAA prefabs using `LOD Group` components, the MeshCollider sits on the root, while the Renderers are nested in child objects. 
  - **Action:** Drag and drop the highest fidelity mesh (e.g., `LOD0`) into the **Target Renderer** slot. This ensures the Gaze Engine calculates physical bounds based on the actual visual geometry, not an empty parent transform.

### 2. Texture Injection: `WSPlacementDynamic`
For flat surfaces (billboards, posters, terminal screens), this script acts as the "painter", pulling network textures and safely injecting them into memory.

- **Target Renderer:** This **MUST** exactly match the Target Renderer slot defined in your `WSPlacementNode`. If they drift, the visual will render in one place while the telemetry engine tracks another, voiding the impression.
- **Texture Property Name:** You must supply the exact **Shader Reference ID**. 
  - *Standard Pipeline:* `_MainTex`
  - *URP / HDRP:* `_BaseMap` or `_BaseColorMap`
- **Material Index:** Zero-indexed array routing for complex meshes. If your mesh has multiple materials, you must tell the engine which sub-mesh receives the network ad. (e.g., Element 1 = Index 1).

#### Handling Texture Atlases & UV Grids
Modern environments often combine multiple textures into a single Atlas Material. When a texture is atlased, Unity relies on the material's **Scale and Transform (ST)** properties, or custom shader properties like `Rows` and `Columns`, to only display a small tile of the larger image. Since injected network ads are single full-resolution images, atlased materials will slice and distort them.

- **Override UV Scale & Offset:** This boolean is checked by default. It instructs the SDK to forcefully inject a `Vector4(1, 1, 0, 0)` into the standard `_ST` component of your Shader Reference ID (e.g., `_BaseMap_ST`), neutralizing basic atlas distortion.
- **Shader Property Overrides:** Many custom Shader Graphs (like those mapping custom grid atlases) use float properties instead of standard `_ST` scaling. If your material has custom atlas properties (like `Rows` or `Tile`), you **must** add them to this list to forcefully reset them to `1` when the ad injects.
  - **How to use:** Add an element to the list. Set the **Value** to `1`.
  - **IMPORTANT - The Property Name:** You must use the internal Shader **Reference Name**, *not* the Display Name shown in the Inspector! The Reference Name almost always begins with an underscore.
  - **Finding the Reference Name:** In the Unity Inspector, you can either click the Gear Icon on the material -> **Select Shader**, OR open the Shader Graph. Click the property in the Blackboard, open Node Settings, and copy the **Reference** string (e.g., `_Rows`, `_GridX`, `_Tile`).

> **Architectural Note:** The engine utilizes a `MaterialPropertyBlock` to execute the texture swap and float overrides. This is a non-destructive operation that prevents memory leaks and ensures your base materials remain untouched in the project hierarchy.

### 3. Global Texture Injection: `WSSharedMaterialNode`
When you have multiple objects in your scene (like 10 banners) that all share the *exact same* Unity Material asset, you can use this script to drastically reduce draw calls and network load. Instead of attaching a node to every single banner, you attach one global node that modifies the shared Material asset directly.

**Step-by-Step Instructions:**
1. **Create an Empty GameObject:** Name it something like `AdController_Billboard_01`.
2. **Add Component:** Attach the `WSSharedMaterialNode` script.
3. **Configure the Node:**
   - **Placement ID:** Paste your unique Supabase placement ID.
   - **Target Material:** Drag the shared `Material` asset directly from your Project window (or from the MeshRenderer of one of the banners) into this slot.
   - **Texture Property Name:** The Shader Reference ID (e.g. `_MainTex`, `_BaseMap`, or `_Map`).
   - **Primary Gaze Target:** The telemetry engine still needs a physical object in the scene to run line-of-sight raycasts against. Pick **just one** of your banners in the scene and drag its Collider into this slot. This will act as the physical anchor for viewability verification.
4. **Atlas & Shader Overrides:** Just like dynamic placements, if your shared Material uses a texture atlas, the single ad image will get cropped.
   - Check **Override UV Scale & Offset** to forcefully hijack standard `_ST` scaling to 1x1.
   - If using custom float properties (like `_Rows` or `_Tile`), add them to the **Shader Property Overrides** list and set their values to `1` so the Material scales to show the full ad!

*When you hit Play, the script will fire off a single network request, download the image, and apply it globally to that Material. All objects using that material will instantly swap to the new ad simultaneously!*

### 3. Rendering 3D Assets (Optional)
To spawn interactive 3D models (like a branded soda can on a table):

1. Attach the `WSPlacement3D` script to an empty anchor GameObject in your scene.
2. Provide the **Placement ID**.
3. The SDK will fetch the AssetBundle, extract the primary GameObject, and instantiate it dynamically as a child of your anchor.

---

## 📞 Support
If you encounter compiler errors or CS0103 reference issues upon importing, ensure your `.asmdef` settings are correct, and your Unity Editor has fully compiled the `WireSyndicate.SDK` assembly. 

For advanced integrations, refer to the official [WireSyndicate Documentation](#).
