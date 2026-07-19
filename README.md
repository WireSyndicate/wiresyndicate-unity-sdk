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
4. Locate the **API Base URL** field. It defaults to `http://localhost:3000` for local testing. For production, update this to the live WireSyndicate API endpoint.

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
Modern environments often combine multiple textures into a single Atlas Material. When a texture is atlased, Unity relies on the material's **Scale and Transform (ST)** properties to only display a small tile of the larger image. 

- **Override UV Scale & Offset:** This boolean is checked by default. It instructs the SDK to forcefully inject a `Vector4(1, 1, 0, 0)` into the `_ST` component of your Shader Reference ID (e.g., `_BaseMap_ST`). 
- **Why this is critical:** By hijacking the UV Scale/Offset math, we ensure the network advertisement stretches perfectly across the entire targeted sub-mesh, completely neutralizing texture atlas distortion. If an ad renders distorted, it will trigger a false impression vulnerability. Leave this checked to guarantee visual integrity.

> **Architectural Note:** The engine utilizes a `MaterialPropertyBlock` to execute the texture swap. This is a non-destructive operation that prevents memory leaks and ensures your base materials remain untouched in the project hierarchy.

### 3. Rendering 3D Assets (Optional)
To spawn interactive 3D models (like a branded soda can on a table):

1. Attach the `WSPlacement3D` script to an empty anchor GameObject in your scene.
2. Provide the **Placement ID**.
3. The SDK will fetch the AssetBundle, extract the primary GameObject, and instantiate it dynamically as a child of your anchor.

---

## 📞 Support
If you encounter compiler errors or CS0103 reference issues upon importing, ensure your `.asmdef` settings are correct, and your Unity Editor has fully compiled the `WireSyndicate.SDK` assembly. 

For advanced integrations, refer to the official [WireSyndicate Documentation](#).
