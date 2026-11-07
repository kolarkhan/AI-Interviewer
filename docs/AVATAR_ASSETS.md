# Avatar asset requirements and integration

## Included interviewers (Microsoft Rocketbox, MIT)

Two realistic, professionally dressed interviewers come from the [Microsoft Rocketbox Avatar Library](https://github.com/microsoft/Microsoft-Rocketbox) (MIT License, (c) 2020 Microsoft). The licence is kept in `Assets/ThirdParty/Rocketbox/LICENSE.md` and credited in-app.

| App id | Rocketbox character | Default voice | Animations |
|---|---|---|---|
| `alex` | `Business_Male_01` (`_facial` version: 15 visemes + ARKit 52) | cedar | idle, listen ×2, talk (male set) |
| `maya` | `Business_Female_01` (`_facial` version) | marin | idle, listen ×2, talk (female set) |

On first open, Unity runs **AI Interviewer › Avatar › Set Up Rocketbox Interviewers** automatically. You can re-run it from the menu at any time. It:
1. imports the models as Humanoid with blendshapes (`RocketboxImportPostprocessor`): textures at 2048 (head) / 1024 (body), Fade hair materials, highest LOD only;
2. builds `Assets/AIInterviewer/Avatars/Rocketbox/{id}_Animator.controller` (Idle / Listening / Thinking / Speaking on the `Mood` parameter);
3. saves a prefab with `BlendShapeAvatarRig` (Rocketbox preset: `AA_VI_*` visemes, `AK_25_JawOpen`, `AK_09/10_EyeBlink*`, `AK_44/45_MouthSmile*`, `AK_03_BrowInnerUp`);
4. writes `Resources/Avatars/{id}.asset`.

If a character is missing from a build, the app falls back to the built-in placeholder avatar (`PlaceholderAvatar.cs`) and labels it.

> Verify on device: the Humanoid auto-mapping of the Biped skeleton, materials under URP (run the Render Pipeline Converter after switching back to URP), and frame rate on mid-range Android.

To add more Rocketbox characters (e.g. `Business_Female_02` or `Business_Male_04`), download the same files into `Assets/ThirdParty/Rocketbox/Avatars/<Name>/`, add an entry in `RocketboxSetup.Avatars`, and add the id to `backend/app/modules/catalog/data.py`.

## Custom or commissioned characters

## Asset contract

| Requirement | Specification |
|---|---|
| Licence | Commercial use in a distributed mobile app; redistribution inside a compiled app allowed. Keep the licence file and record it in `AvatarDefinition.licenseNotice` (shown in Credits). |
| Style | Stylized-realistic professional, business clothing, friendly neutral expression |
| Rig | Humanoid skeleton importable as Unity **Humanoid** (hips, spine, chest, neck, head, arms, hands). Eye bones recommended; jaw bone optional. |
| Facial blendshapes | Either the 15 Oculus visemes (`viseme_sil … viseme_U`) **or** ARKit 52 (`jawOpen`, `mouthFunnel`, `mouthPucker`, `mouthClose`, `mouthSmileLeft/Right`, `eyeBlinkLeft/Right`, `browInnerUp`, …). Minimum: `jawOpen` (or jaw bone) + blink. |
| Triangles | 20–50k total (face/head ~10–15k); ≤ 60k hard cap for mid-range Android |
| Materials | ≤ 4 materials, URP Lit (or Simple Lit on low tier), textures ≤ 2048² (1024² for clothing), ASTC compression |
| Scale/orientation | 1 unit = 1 m, facing +Z, feet at origin, ~1.75 m tall (seated pose acceptable) |
| Animations | Separate clips: seated idle (breathing), listening (subtle nods), thinking (glance aside), talking gestures. Loopable, upper body. |

### Sources to evaluate (verify licence terms yourself before purchase)

- **Character Creator 4 / Reallusion** export with ARKit or viseme blendshapes (commercial licence required).
- **Marketplace assets** (Unity Asset Store, CGTrader, Sketchfab) with explicit commercial licence and blendshapes.
- **Custom commission** (Blender artist) using this contract as the brief.
- Avatar SDK services: check that their terms allow embedding in a paid app and that the vendor is still maintained.

Do not use assets without a clear licence. Do not depict a real person.

## Integration steps

1. **(Optional) Prepare in Blender:** `blender --background model.blend --python tools/blender/prepare_avatar.py -- --report-only` audits rig, shape keys and triangle count. Use `--rename mapping.json` to map vendor shape-key names to `viseme_*` / ARKit names, `--decimate 0.5` to reduce non-face meshes, then export FBX.
2. **Import** the FBX into `Assets/AIInterviewer/Avatars/<Name>/`. In the model importer set *Rig → Animation Type: Humanoid* and check *Import BlendShapes*. Configure the avatar mapping, including the eye and jaw bones if present.
3. **Animator:** run *AI Interviewer › Avatar › Create Interviewer Animator Controller*. Assign your clips to the Idle, Listening, Thinking and Speaking states. The app sets the `Mood` (int 0–3) and `Talk` (float) parameters.
4. **Prefab:** create a prefab from the model. Add **BlendShapeAvatarRig** to the root, assign the face/teeth/tongue `SkinnedMeshRenderer`s and the Animator, then use the component's context menu *Fill viseme map: Oculus* or *ARKit approximation*.
5. **Validate:** select the prefab and run *AI Interviewer › Avatar › Validate Selected Avatar Prefab*. Fix every ERROR.
6. **Register:** *Assets › Create › AI Interviewer › Avatar Definition*. Set `id` to the backend avatar id (`jordan`, `sam`, or a new id), assign the prefab, adjust `positionOffset` so the character sits behind the desk, and save it at `Assets/AIInterviewer/Resources/Avatars/<id>.asset`.
7. **Backend:** set `available: True` for that avatar in `backend/app/modules/catalog/data.py` (and add it to the plan's `avatars` tuple in `billing/plans.py` if it's new).
8. **Test** on device: lip-sync (see `LIPSYNC_AND_AUDIO.md`), frame rate, memory.

## How the app drives the avatar

| Behaviour | Implementation |
|---|---|
| Lip-sync | `IAvatarLipSync.Tick` → `VisemeFrame` (15 visemes + jaw) → `BlendShapeAvatarRig.ApplyVisemes` |
| Blinking | random 2.2–5.5 s, 18 % double blinks, fast close / slow open |
| Gaze | saccades; eye contact while listening, glance aside while thinking |
| Head & breathing | layered Perlin sway, listening tilt and nods, speech-energy emphasis; chest breathing scale |
| Moods | Idle, Listening, Thinking, Speaking — from the interview state machine, with 0.3 s transitions |
| Expressions | smile/brow blendshapes eased per mood |
