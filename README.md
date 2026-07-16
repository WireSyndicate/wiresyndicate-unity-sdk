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
2. Click **Add Component** and attach the `WireSyndicateInitializer` script.
3. In the Inspector, locate the **Network Key** field.
4. Paste your **Organization ID** (found in your WireSyndicate Developer Portal) into the Network Key field.

*When the scene plays, the Initializer will automatically trigger the Ephemeral Token Handshake and lock the session.*

---

## 🎯 Creating Placements

Once the SDK is initialized, you can map physical ad spaces in your 3D world.

### 1. Attaching the Placement Node
The `WSPlacementNode` is the master anchor for your ad spaces. It tells the Gaze Verification Engine where the asset lives in 3D space.

1. Create a 3D object in your scene (e.g., a Quad or a Cube) where you want the ad to appear.
2. Ensure it has a **Renderer** and a **Collider** (the Engine requires these to calculate bounds and occlusion).
3. Click **Add Component** and attach the `WSPlacementNode` script.
4. In the Inspector, paste the **Placement ID** associated with this specific ad unit (generated from your Developer Portal).

### 2. Rendering Dynamic Textures
To render dynamic 2D images (like posters or billboards) onto your placement:

1. Attach the `WSPlacementDynamic` script to the same object.
2. Ensure the **Placement ID** matches the ID on the `WSPlacementNode`.
3. The SDK will automatically fetch the active contract texture, securely cache it to disk, and non-destructively swap the Material Property Block at runtime.

### 3. Rendering 3D Assets (Optional)
To spawn interactive 3D models (like a branded soda can on a table):

1. Attach the `WSPlacement3D` script to an empty anchor GameObject in your scene.
2. Provide the **Placement ID**.
3. The SDK will fetch the AssetBundle, extract the primary GameObject, and instantiate it dynamically as a child of your anchor.

---

## 📞 Support
If you encounter compiler errors or CS0103 reference issues upon importing, ensure your `.asmdef` settings are correct, and your Unity Editor has fully compiled the `WireSyndicate.SDK` assembly. 

For advanced integrations, refer to the official [WireSyndicate Documentation](#).
