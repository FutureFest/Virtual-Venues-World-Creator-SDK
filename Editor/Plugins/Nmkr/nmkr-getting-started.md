# NMKR Plugin — Getting Started

The NMKR plugin lets your world mint and distribute Cardano NFTs to players from
inside the experience. Drop an interactable in your scene, point it at one of
your NMKR Studio projects, and players can mint to their linked wallet or to
their in-world Stash with one click.

The plugin runs on top of [NMKR Studio](https://www.nmkr.io). Your NMKR account
is the source of truth for the NFT collection (project), per-NFT metadata, mint
credits, and payout wallet. The plugin is the bridge that wires your world to it.

## Prerequisites

Before you start, you'll need:

- **An NMKR Studio account.** Create one at [studio.nmkr.io](https://studio.nmkr.io).
  Preprod and mainnet are separate NMKR accounts with separate API keys, so if
  you plan to ship to mainnet you'll want both.
- **Your NMKR Customer ID.** Visible in NMKR Studio under your account profile.
- **An API key.** Generate one in NMKR Studio under *API Keys*. Treat it like a
  password — anyone with this key can spend mint credits on your account.
- **A configured project.** A "project" in NMKR Studio is one NFT collection.
  Upload your NFTs, set the policy, and copy the Project UID — you'll paste it
  into your scene later.
- **Mint credits** (mainnet) or **free preprod credits** for testing. NMKR
  Studio shows your balance on the dashboard.
- **A Cardano payout wallet address** for any ADA proceeds.

## Step 1 — Configure your NMKR account

1. Open **`VirtualVenues > Plugins > NMKR Settings`**.
2. If no config exists yet, click **Create**. The plugin will create
   `Assets/VirtualVenuesPluginConfigs/Resources/NmkrPluginConfig.asset` for
   you. This asset is local to your project — it's not part of the VV SDK
   package.
3. Pick the **Environment** you want to configure (`Preprod` for testing,
   `Mainnet` for production). Each environment stores its own account, so you
   can fill them both in and toggle between them.
4. Fill in **Customer ID**, **API Key**, **Payout Wallet Address**, and
   **Display Name** for the active environment.
5. Click **Test Connection** to verify the credentials are accepted by NMKR.
6. Click **Save**.

> The same form is available on the NMKR Plugin's Inspector once you add it to a
> scene — both edit the same `NmkrPluginConfig` asset, so settings never drift.

## Step 2 — Add the NMKR Plugin to your scene

The NMKR Plugin is a small in-scene settings carrier that travels with your
published world. Without it, the plugin has no way to know which NMKR account
to use at runtime.

1. In your world scene, right-click in the Hierarchy and pick
   **`VirtualVenues > Plugins > NMKR > Add NMKR Plugin`**
   *(or use the same path from the `GameObject` menu)*.
2. A `NMKR Plugin` GameObject appears at the scene root. Leave it there.

That's it — at build time, the plugin will copy your `NmkrPluginConfig` values
onto this GameObject so they ride along inside the published world.

Only one NMKR Plugin per scene is meaningful. If you run the menu again, the
plugin will select the existing one instead of spawning a duplicate.

## Step 3 — Add a Mint Interactable

A Mint Interactable is the in-world object players walk up to and trigger the
mint from. You can place as many as you want — each one can target a different
NMKR project.

1. Right-click in the Hierarchy (typically under a Stage group, or wherever you
   want the mint point to live) and pick
   **`VirtualVenues > Plugins > NMKR > Add Mint Interactable`**.
2. A `NMKR Mint Interactable` GameObject appears, pre-wired with:
   - A cube **Mesh** (replace with your own art).
   - A trigger **BoxCollider** sized 2×2×2 — the proximity area players need to
     enter to interact.
   - A **Context Popup Anchor** child — used to position the "Press E to
     interact" prompt above the object.
3. In the Inspector, set **Project UID** — the dropdown lists every project on
   your configured NMKR account. Pick the collection you want this interactable
   to mint from.
4. Set **NFT Count** — the per-player cap for this drop (default 1).
5. Optionally fill **Drop Title**, **Drop Description**, **Drop Image** for
   display in the mint popup.
6. Replace the placeholder cube Mesh with your own art if you like. As long as
   the trigger collider stays wired into the `Interaction Collider` field, the
   proximity detection keeps working.

## Step 4 — Test in preprod

Always test on preprod first. Preprod runs on Cardano's test network with free
test ADA, so mistakes don't cost real money or burn mint credits.

1. Make sure your **NMKR Settings → Environment** is set to `Preprod` and
   you've saved.
2. Publish your world (or test it via your usual local-publish flow).
3. Join the world as a player and walk up to your Mint Interactable.
4. Press the interact key — the Mint Popup appears with three options:
   - **Select Wallet** — pick from wallets you've linked to your account.
   - **Send to Address** — paste any Cardano address manually.
   - **Send to Stash** — mint into your in-world managed wallet (Stash). If you
     don't have one yet, it's created automatically on first use.
5. Click **Mint**. On success, you'll see a confirmation panel.
6. Open the Stash widget to see your minted NFT alongside its ADA balance.
7. Verify the NFT in [pool.pm/preprod](https://preprod.pool.pm) or your wallet.

## Component reference

### NMKR Plugin Settings

The component on the `NMKR Plugin` GameObject. Fields on the component are
intentionally empty in your scene — the values are stamped in at build time
from `NmkrPluginConfig`, so editing them in the Inspector is read-only by
design (the Inspector shows the shared settings form instead).

| Property | Description |
|---|---|
| Customer ID | Your NMKR Studio customer ID. |
| API Key | Your NMKR API key. **Stamped only into the UMS bundle** — never ships to players. |
| Environment | `Preprod` or `Mainnet`. Picks which NMKR backend to talk to. |
| Payout Wallet Address | Where ADA from any future sales lands. |
| Display Name | Friendly name shown in logs/UI. |

### NMKR Mint Interactable

Extends the standard SDK `Interactable`, so all the highlight, popup, and
interact-event behaviour you're used to applies. NMKR-specific fields:

| Property | Description |
|---|---|
| Project UID | The NMKR project this interactable mints from. Dropdown lists every project on your configured account. |
| NFT Count | Per-player mint cap for this drop. The mint popup disables once a player hits it. |
| Drop Title / Description / Image | Optional display metadata for the mint popup. |

Standard `Interactable` fields you can tweak:

| Property | Description |
|---|---|
| Interaction Display Text | Prompt shown when a player is in range (e.g. `"Press {0} to interact"`). |
| Context Popup Parent | Anchor for the interact prompt. Defaults to the `Context Popup Anchor` child. |
| Interaction Collider | Trigger volume the player must enter. Pre-wired to the prefab's `BoxCollider`. |
| Highlight Renderers | Renderers toggled on when the player approaches. |
| On Highlight / On Unhighlight | Custom UnityEvents for visual feedback. |
| On Local Interact | UnityEvent fired on local interaction — the NMKR mint popup hook is attached automatically; add your own listeners freely. |

## How it works at runtime

Knowing the architecture isn't required to ship a drop, but it's useful when
debugging.

1. When the world loads, an `NmkrPlugin` component auto-spawns and reads the
   `NmkrPluginSettings` on your `NMKR Plugin` GameObject.
2. On the dedicated server (UMS) build, the plugin initialises the NMKR SDK
   with your API key. On the player client (UPC) build, the API key is empty
   by design — the player never holds it.
3. The Mint Interactable Manager attaches a runtime handler to every
   `NmkrMintInteractable` in the scene. When a player interacts, the handler
   opens the singleton **Mint Popup**.
4. The Mint Popup gathers the player's choice (wallet / address / stash) and
   asks the UPC client to perform the mint.
5. The UPC client sends a Mirror RPC to the UMS server. UMS holds the API key,
   calls NMKR, and returns the result back over Mirror.
6. The result is shown in the confirmation panel; the Stash UI refreshes on
   next open.

## Security notes

- **The API key never ships to players.** A build-time processor strips the
  key from every build target except `StandaloneLinux64` (the dedicated
  server). If you ever inspect your WebGL bundle and find a key, file a bug —
  that's a regression.
- **Keep the `NmkrPluginConfig` asset out of public source control.** The
  configured location (`Assets/VirtualVenuesPluginConfigs/`) is outside the VV
  SDK package on purpose so you can gitignore it locally.
- **Use preprod for development.** Preprod credentials and mainnet credentials
  live in separate slots on the same config — you can have both filled and
  toggle the active environment without losing either.
- **Rotate keys on suspicion.** If you think your key has been exposed, revoke
  and regenerate it in NMKR Studio. The Test Connection button will tell you
  immediately if the key was rejected.

## Troubleshooting

**The Settings window shows "No NmkrPluginConfig found."**
Click **Create** in the window. It'll generate the asset at
`Assets/VirtualVenuesPluginConfigs/Resources/NmkrPluginConfig.asset`.

**Test Connection fails with "Unauthorized" / 401.**
Customer ID or API Key is wrong, or you're testing a preprod key against
mainnet (or vice versa). Re-check both fields and the Environment selector.

**The Project UID dropdown is empty.**
Either credentials aren't valid, or your NMKR account has no projects yet.
Create one in NMKR Studio first.

**Players see "Stash wallet not found" when minting to Stash.**
The Stash is created on first mint. If creation keeps failing, check the UMS
logs — the most common cause is a name collision (a wallet with the same name
already exists on the NMKR account from an earlier test run). Renaming the
player or clearing the test wallet in NMKR Studio resolves it.

**Players see "Mint limit reached!" but you set a generous cap.**
The cap is tracked per-player per-project in Unity Cloud Save. Changing the
cap on the interactable doesn't reset existing counters. For testing, clearing
the player's local cloud-save data for the `MintCount_{ProjectUid}` key
resets them.

**The Stash shows tokens but the card image is a purple placeholder.**
Expected for v1 — the NFT image loader isn't wired up yet. Token names and
quantities are correct; the visual will arrive in a future update.

## Known limitations (v1)

- **Host-paid mint flow.** Today the host's NMKR account front-pays the mint
  credits and ADA. Audience-paid checkout via `pay.nmkr.io` is on the roadmap.
- **Client-side mint cap.** The per-player cap is enforced in the client via
  Cloud Save. Players can't realistically bypass it in the current build flow,
  but NMKR-side sale conditions will replace this in a future release.
- **Cardano only.** The plugin targets Cardano via NMKR. Other chains aren't
  supported.
- **No secondary market.** The plugin handles primary mints only. Re-listing
  and direct-sale flows aren't included.

## Support

For NMKR Studio account issues (mint credits, project setup, payout):
[support.nmkr.io](https://support.nmkr.io).

For VV SDK plugin issues: contact the VirtualVenues development team.
