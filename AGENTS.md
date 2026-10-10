# VirtualVenues SDK — notes for AI agents

You are in a creator's Unity project. This SDK (`com.virtualvenues.sdk`) is installed at
`Packages/com.virtualvenues.sdk/`. It lets you build a VirtualVenues world (a Unity scene full of SDK
components) and publish it to the VirtualVenues player app.

Drive the Unity Editor with the `unity` CLI (`unity status`, `unity command eval '<C#>'`). Do not hand-edit
`.unity` / `.prefab` YAML.

Read in this order:
1. `Documentation~/Building-a-World.md` — set up the project, place components from code, test, traps.
2. `Documentation~/Components.md` — every world component: what it does, how to add it, its fields.
3. `Documentation~/Publishing.md` — sign-in, `WorldPublisher.WorldPublishJob` (build + upload from code).
4. `Documentation~/Rides.md` — spline tracks, roller coasters, support posts.
5. `Documentation~/Paths.md` — pathways: a ground strip plus lamp posts, fences and plants laid out along a spline.
5. `Documentation~/Paths.md` — pathways: ground strip + lamp posts, fences, plants laid out along a spline.

The human-facing overview is `README.md`; publish requirements are in `REQUIREMENTS.md`.
