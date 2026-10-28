using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Jotunn.Managers;
using Jotunn.Utils;
using TMPro;
using UnityEngine;

namespace Haugbui.Structures
{
    internal static class StructureVisuals
    {
        public const string RootName = "HaugbuiVisual";

        internal static readonly HashSet<string> MissingParts = new HashSet<string>();

        /// <summary>"pieceId/part" → what was copied and why meshes were skipped; read by the self-test.</summary>
        internal static readonly Dictionary<string, string> PartDiagnostics = new Dictionary<string, string>();

        internal static int CopiedCount(string pieceId, string part) =>
            PartDiagnostics.TryGetValue(pieceId + "/" + part, out string text) && text.StartsWith("copied ")
                ? int.Parse(text.Substring(7).Split(';')[0])
                : -1;

        /// <summary>Idempotent: hides the host meshes when requested and adds copies of the parts' meshes.</summary>
        public static void Apply(GameObject target, StructureDefinition definition)
        {
            if (target.transform.Find(RootName) != null) return;
            if (!string.IsNullOrEmpty(definition.HostAlbedoFile) || !string.IsNullOrEmpty(definition.HostNormalFile))
                StyleHost(target, definition.HostAlbedoFile, definition.HostNormalFile);
            if (!string.IsNullOrEmpty(definition.FloatingText))
                AddFloatingMark(target, definition.FloatingText);
            if (definition.Parts == null || definition.Parts.Length == 0) return;

            float? hostTop = definition.HideHost ? (float?)null : LocalTop(target);
            if (definition.HideHost)
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(target.transform, false);
            Vector3 anchor = new Vector3(0, hostTop ?? 0f, 0);

            int copied = 0;
            float? surface = null;
            foreach (VisualPart part in definition.Parts)
            {
                GameObject source = FindPart(part.Prefab);
                if (source == null)
                {
                    if (MissingParts.Add(part.Prefab)) Log.Warning($"Visual part '{part.Prefab}' not found for {definition.PieceId}");
                    continue;
                }
                var holder = new GameObject(part.Prefab);
                holder.transform.SetParent(root.transform, false);
                Vector3 place = anchor + part.Offset;
                if (part.OnTop && surface != null) place = new Vector3(part.Offset.x, surface.Value + part.Offset.y, part.Offset.z);
                holder.transform.localPosition = place;
                holder.transform.localRotation = Quaternion.Euler(part.Euler);
                holder.transform.localScale = Vector3.one * part.Scale;
                copied += CopyMeshes(source, holder.transform, definition.PieceId + "/" + part.Prefab, part);
                if (!string.IsNullOrEmpty(part.EmissionFile)) VeinPulse.Attach(holder);
                if (!part.OnTop)
                {
                    float? top = LocalTop(holder);
                    if (top != null) surface = holder.transform.localPosition.y + top.Value * holder.transform.localScale.y;
                }
                if (part.ItemAttach) PoseOnItemStand(target, source, holder.transform);
                if (!string.IsNullOrEmpty(part.GlowFrom)) CopyGlow(part.GlowFrom, holder.transform, part.Emission ?? StructureDefinitions.HaugbuiPurple);
                if (part.Aura) AddAura(holder.transform);
                if (part.Mist) CopyWraithMist(holder.transform);
            }

            if (copied == 0 && definition.HideHost)
            {
                Log.Warning($"{definition.PieceId}: no visual parts found; showing the host instead.");
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = true;
            }
        }

        public const string MarkName = "HaugbuiMark";

        /// <summary>
        /// The sign's icon is a world-space text canvas. Its only mesh is the board, which is not copied.
        /// Cloning the sign itself would add a networked object the player can edit.
        /// </summary>
        private static void AddFloatingMark(GameObject target, string richText)
        {
            if (target.transform.Find(MarkName) != null) return;
            GameObject sign = PrefabManager.Cache.GetPrefab<GameObject>("sign");
            Transform canvas = sign != null ? sign.transform.Find("Canvas") : null;
            if (canvas == null)
            {
                Log.Warning("Sign canvas missing; no floating mark on " + target.name);
                return;
            }
            GameObject copy = Object.Instantiate(canvas.gameObject, target.transform, false);
            copy.name = MarkName;
            const float scale = 1.728f;
            float y = (LocalTop(target) ?? 0.8f) + 0.36f;
            copy.transform.localPosition = new Vector3(0f, y, 0f);
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one * scale;
            TextMeshProUGUI text = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null) return;
            text.color = Color.white;
            text.text = richText;
            text.raycastTarget = false;
            if (text.fontMaterial != null && text.fontMaterial.HasProperty("_FaceColor"))
                text.fontMaterial.SetColor("_FaceColor", Color.white);
            MarkFloat.Attach(copy.transform);
        }

        /// <summary>
        /// The wood chest shares one material with every vanilla chest, and the open lid uses it too.
        /// The paint is a copy, so the vanilla chests stay as they are.
        /// </summary>
        private static void StyleHost(GameObject target, string albedoFile, string normalFile)
        {
            Material painted = null;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material source = mats[i];
                    if (!UsesWoodChest(source)) continue;
                    if (painted == null)
                    {
                        painted = new Material(source) { name = source.name + " (Haugbui)" };
                        ApplyAlbedo(painted, source, albedoFile);
                        ApplyBump(painted, source, normalFile);
                    }
                    mats[i] = painted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }

        private static bool UsesWoodChest(Material source)
        {
            if (source == null || !source.HasProperty(MainTex)) return false;
            Texture tex = source.GetTexture(MainTex);
            return tex != null && tex.name.IndexOf("woodchest", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject FindPart(string name)
        {
            string[] names = name == "piece_chest_treasure" ? new[] { name, "piece_chesttreasure" }
                : name == "piece_blackmarble_column_2" ? new[] { name, "blackmarble_column_2" }
                : new[] { name };
            foreach (string candidate in names)
            {
                GameObject found = PrefabManager.Cache.GetPrefab<GameObject>(candidate);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Highest point of the visible host meshes in the root's space; works on prefabs outside the scene.</summary>
        private static float? LocalTop(GameObject target)
        {
            float? top = null;
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = target.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).y;
                    if (top == null || y > top) top = y;
                }
            }
            return top;
        }

        /// <summary>
        /// Copies static and skinned meshes as plain meshes. Instantiating the source prefab would create a networked
        /// object. Item prefabs keep their world model under "attach"; inactive children are alternate models.
        /// </summary>
        private static int CopyMeshes(GameObject source, Transform parent, string diagnosticsKey, VisualPart part)
        {
            int count = 0;
            var skipped = new List<string>();
            GameObject floater = source.GetComponent<TombStone>()?.m_floater;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(renderer.transform, source.transform);
                bool underChild = string.IsNullOrEmpty(part.OnlyChild) || UnderNamed(renderer.transform, source.transform, part.OnlyChild);
                if (!underChild)
                {
                    skipped.Add($"{path} (not {part.OnlyChild})");
                    continue;
                }
                bool openPiece = part.OpenChest && path.IndexOf("open", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool closedLid = part.OpenChest && !openPiece && IsLid(path);
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh
                    : null;
                string reason = mesh == null ? renderer.GetType().Name + " without mesh"
                    : floater != null && renderer.transform.IsChildOf(floater.transform) ? "floater"
                    : closedLid ? "closed lid"
                    : IsDistanceLod(renderer.transform, source.transform) ? "distance lod"
                    : UnderNamed(renderer.transform, source.transform, "ashlayer") ? "ash"
                    : openPiece || !string.IsNullOrEmpty(part.OnlyChild) ? null
                    : HiddenReason(renderer.transform, source.transform);
                if (reason != null)
                {
                    skipped.Add($"{path} ({reason})");
                    continue;
                }

                var copy = new GameObject(renderer.name);
                copy.transform.SetParent(parent, false);
                copy.transform.localPosition = source.transform.InverseTransformPoint(renderer.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * renderer.transform.rotation;
                copy.transform.localScale = renderer.transform.lossyScale;
                bool styled = part.Emission.HasValue || part.Albedo.HasValue || part.Tint != PartTint.None;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = styled
                    ? renderer.sharedMaterials.Select(m => Style(m, part, diagnosticsKey)).ToArray()
                    : renderer.sharedMaterials;
                count++;
            }

            if (!PartDiagnostics.ContainsKey(diagnosticsKey))
                PartDiagnostics[diagnosticsKey] = $"copied {count}" + (skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped) : "");
            return count;
        }

        private static readonly Dictionary<string, Material> TintCache = new Dictionary<string, Material>();

        /// <summary>"pieceId/part" → shader and emission data of tinted materials; read by the self-test.</summary>
        internal static readonly Dictionary<string, string> MaterialDiagnostics = new Dictionary<string, string>();

        /// <summary>
        /// Works on a copy: the source material is shared with every vanilla object using it, so editing it in
        /// place would recolour those objects too.
        /// </summary>
        private static Material Style(Material source, VisualPart part, string diagnosticsKey)
        {
            if (part.Tint == PartTint.Clean) return CleanCopy(source, part.AlbedoFile, part.EmissionFile);
            if (part.Tint == PartTint.SilverBar) return SilverBarCopy(source);
            if (part.Tint == PartTint.Lead && IsGem(source)) return source;
            if (part.Tint == PartTint.PurpleVeins && !HasEmissionMap(source)) return source;
            Material copy = Tinted(source, part.Emission, part.Albedo, diagnosticsKey);
            if (copy == source && (part.EmissionMask || !string.IsNullOrEmpty(part.AlbedoFile)))
                copy = new Material(source) { name = source.name + " (Haugbui)" };
            if (part.EmissionMask) ApplyEmissionMask(copy, source);
            if (!string.IsNullOrEmpty(part.AlbedoFile)) ApplyAlbedo(copy, source, part.AlbedoFile);
            if (part.Tint == PartTint.None) return copy;
            if (copy == source) copy = new Material(source) { name = source.name + " (Haugbui)" };
            if (part.Tint == PartTint.Silver)
            {
                if (copy.HasProperty(ColorProperty)) copy.SetColor(ColorProperty, new Color(0.78f, 0.80f, 0.84f));
                if (copy.HasProperty(Metallic)) copy.SetFloat(Metallic, 0.9f);
                if (copy.HasProperty(Glossiness)) copy.SetFloat(Glossiness, 0.75f);
                if (copy.HasProperty(EmissionColor))
                {
                    copy.SetColor(EmissionColor, new Color(0.45f, 0.47f, 0.5f));
                    copy.EnableKeyword("_EMISSION");
                }
            }
            else if (part.Tint == PartTint.Lead)
            {
                if (copy.HasProperty(ColorProperty)) copy.SetColor(ColorProperty, StructureDefinitions.HaugbuiLead);
                if (copy.HasProperty(Metallic)) copy.SetFloat(Metallic, 0.65f);
            }
            else if (part.Tint == PartTint.PurpleVeins && copy.HasProperty(EmissionColor))
            {
                // Keep the groove mask. A flat colour lights the whole stone; the map already picks out the carvings.
                Color original = source.GetColor(EmissionColor);
                float intensity = Mathf.Max(1.6f, Mathf.Max(original.r, Mathf.Max(original.g, original.b)));
                copy.SetColor(EmissionColor, new Color(0.55f, 0.08f, 1f) * intensity);
                copy.EnableKeyword("_EMISSION");
            }
            else if (part.Tint == PartTint.LightMarble)
            {
                if (copy.HasProperty(ColorProperty)) copy.SetColor(ColorProperty, new Color(1.7f, 1.66f, 1.6f));
                if (copy.HasProperty(EmissionColor))
                {
                    copy.SetColor(EmissionColor, new Color(0.22f, 0.22f, 0.24f));
                    copy.EnableKeyword("_EMISSION");
                }
            }
            return copy;
        }

        private static Material _silverBarCopy;

        /// <summary>
        /// The silver ingot has no colour texture. Its shine is this material: white, fully metallic, high gloss, emission off.
        /// </summary>
        private static Material SilverBarCopy(Material fallback)
        {
            if (_silverBarCopy != null) return _silverBarCopy;
            MeshRenderer renderer = PrefabManager.Cache.GetPrefab<GameObject>("Silver")?.GetComponentInChildren<MeshRenderer>();
            Material bar = renderer != null ? renderer.sharedMaterial : null;
            if (bar == null)
            {
                Log.Warning("Silver bar material not found; bracelets keep their own material.");
                return fallback;
            }
            _silverBarCopy = new Material(bar) { name = bar.name + " (Haugbui)" };
            return _silverBarCopy;
        }

        /// <summary>A new material from the prefab as it ships, with any glow this mod added before stripped off.</summary>
        private static Material CleanCopy(Material source, string albedoFile, string emissionFile)
        {
            var copy = new Material(source) { name = source.name + " (Haugbui clean)" };
            Texture2D glow = string.IsNullOrEmpty(emissionFile) ? null : VeinGlow(source, emissionFile);
            if (glow != null && copy.HasProperty(EmissionMap))
            {
                copy.EnableKeyword("_EMISSION");
                copy.SetTexture(EmissionMap, glow);
                if (copy.HasProperty(EmissionColor)) copy.SetColor(EmissionColor, VeinPulse.Full);
            }
            else
            {
                copy.DisableKeyword("_EMISSION");
                if (copy.HasProperty(EmissionColor)) copy.SetColor(EmissionColor, Color.black);
                if (copy.HasProperty(EmissionMap)) copy.SetTexture(EmissionMap, null);
            }
            ApplyAlbedo(copy, source, albedoFile);
            return copy;
        }

        private static readonly Dictionary<int, Texture2D> EmissionMasks = new Dictionary<int, Texture2D>();

        /// <summary>The rune glow is stored purple inside the emission map, so a blue colour alone stays purple.</summary>
        private static void ApplyEmissionMask(Material copy, Material source)
        {
            if (copy == null || copy == source || !copy.HasProperty(EmissionMap)) return;
            Texture current = source != null && source.HasProperty(EmissionMap) ? source.GetTexture(EmissionMap) : null;
            if (!(current is Texture2D texture)) return;
            int id = texture.GetInstanceID();
            if (!EmissionMasks.TryGetValue(id, out Texture2D mask))
            {
                mask = LuminanceMask(texture);
                EmissionMasks[id] = mask;
            }
            if (mask != null) copy.SetTexture(EmissionMap, mask);
        }

        private static Texture2D LuminanceMask(Texture2D source)
        {
            Texture2D readable = AssetUtils.DuplicateTexture(source);
            if (readable == null) return null;
            Color[] pixels = readable.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                float strength = Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b));
                pixels[i] = new Color(strength, strength, strength, pixels[i].a);
            }
            readable.SetPixels(pixels);
            readable.Apply(false, true);
            readable.name = source.name + " (Haugbui mask)";
            return readable;
        }

        private static readonly Dictionary<string, Texture2D> VeinComposites = new Dictionary<string, Texture2D>();

        /// <summary>
        /// The purple mask is drawn on the chest's own glow map. Point filtering keeps that paint off the
        /// neighbouring texels, which are the skull faces.
        /// </summary>
        private static Texture2D VeinGlow(Material source, string emissionFile)
        {
            if (VeinComposites.TryGetValue(emissionFile, out Texture2D cached)) return cached;
            Texture2D veins = LoadAlbedo(emissionFile);
            if (veins == null) return null;
            Texture2D glow = veins;
            Texture original = source != null && source.HasProperty(EmissionMap) ? source.GetTexture(EmissionMap) : null;
            if (original is Texture2D originalTexture)
            {
                Texture2D readable = AssetUtils.DuplicateTexture(originalTexture);
                if (readable != null && readable.width == veins.width && readable.height == veins.height)
                {
                    Color[] basePixels = readable.GetPixels();
                    Color[] veinPixels = veins.GetPixels();
                    for (int i = 0; i < basePixels.Length; i++)
                    {
                        Color vein = veinPixels[i];
                        if (vein.r > 0.5f || vein.g > 0.5f || vein.b > 0.5f)
                            basePixels[i] = Color.white;
                    }
                    readable.SetPixels(basePixels);
                    readable.Apply(false, true);
                    glow = readable;
                }
            }
            glow.filterMode = FilterMode.Point;
            glow.wrapMode = TextureWrapMode.Clamp;
            glow.name = emissionFile;
            VeinComposites[emissionFile] = glow;
            return glow;
        }

        private static readonly Dictionary<string, Texture2D> AlbedoFiles = new Dictionary<string, Texture2D>();

        private static void ApplyAlbedo(Material copy, Material source, string file)
        {
            Texture2D replacement = LoadAlbedo(file);
            if (replacement == null || !copy.HasProperty(MainTex)) return;
            Texture original = source != null && source.HasProperty(MainTex) ? source.GetTexture(MainTex) : null;
            if (original != null)
            {
                replacement.wrapMode = original.wrapMode;
                replacement.filterMode = original.filterMode;
                replacement.anisoLevel = original.anisoLevel;
            }
            copy.SetTexture(MainTex, replacement);
        }

        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");

        private static void ApplyBump(Material copy, Material source, string file)
        {
            Texture2D replacement = LoadAlbedo(file);
            if (replacement == null || copy == null || !copy.HasProperty(BumpMap)) return;
            Texture original = source != null && source.HasProperty(BumpMap) ? source.GetTexture(BumpMap) : null;
            if (original != null)
            {
                replacement.wrapMode = original.wrapMode;
                replacement.filterMode = original.filterMode;
            }
            copy.SetTexture(BumpMap, replacement);
            copy.EnableKeyword("_NORMALMAP");
        }

        private static Texture2D LoadAlbedo(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (AlbedoFiles.TryGetValue(file, out Texture2D cached)) return cached;
            string directory = Path.GetDirectoryName(typeof(StructureVisuals).Assembly.Location);
            string path = string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "textures", file);
            if (path == null || !File.Exists(path))
            {
                Log.Warning($"Albedo {file} is missing; the part keeps the vanilla texture.");
                AlbedoFiles[file] = null;
                return null;
            }
            var texture = AssetUtils.LoadTexture(path, false);
            if (texture == null)
            {
                Log.Warning($"Albedo {file} could not be loaded; the part keeps the vanilla texture.");
                AlbedoFiles[file] = null;
                return null;
            }
            texture.name = file;
            Log.Info($"Albedo {file} loaded ({texture.width}x{texture.height}).");
            AlbedoFiles[file] = texture;
            return texture;
        }

        private static bool IsGem(Material source)
        {
            string name = source != null ? source.name.ToLowerInvariant() : "";
            if (name.Contains("gem") || name.Contains("ruby") || name.Contains("gold") || name.Contains("amber")
                || name.Contains("emerald") || name.Contains("jewel") || name.Contains("crystal"))
                return true;
            if (source == null || !source.HasProperty(ColorProperty)) return false;
            Color color = source.GetColor(ColorProperty);
            float max = Mathf.Max(color.r, color.g, color.b);
            float min = Mathf.Min(color.r, color.g, color.b);
            return max > 0.25f && (max - min) / max > 0.45f;
        }

        private static bool IsDark(Material source)
        {
            if (source == null) return false;
            string name = source.name.ToLowerInvariant();
            if (name.Contains("black") || name.Contains("dark") || name.Contains("vein")) return true;
            if (!source.HasProperty(ColorProperty)) return false;
            Color color = source.GetColor(ColorProperty);
            return Mathf.Max(color.r, color.g, color.b) < 0.22f;
        }

        private static bool HasEmissionMap(Material source) =>
            source != null && source.HasProperty(EmissionMap) && source.GetTexture(EmissionMap) != null;

        private static bool IsLid(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("lid") || lower.Contains("top") || lower.Contains("door") || lower.Contains("cover") || lower.Contains("flap");
        }

        /// <summary>lod1 and the rune table's "low" repeat the piece. Copying them throws a second object when it breaks.</summary>
        private static bool IsDistanceLod(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                if (t.name.Equals("lod1", System.StringComparison.OrdinalIgnoreCase)
                    || t.name.Equals("lod2", System.StringComparison.OrdinalIgnoreCase)
                    || t.name.Equals("lod3", System.StringComparison.OrdinalIgnoreCase)
                    || t.name.Equals("low", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            Renderer renderer = node.GetComponent<Renderer>();
            if (renderer == null) return false;
            LODGroup group = renderer.GetComponentInParent<LODGroup>();
            if (group == null || (group.transform != root && !group.transform.IsChildOf(root))) return false;
            LOD[] lods = group.GetLODs();
            if (lods == null || lods.Length < 2 || ListedIn(lods[0], renderer)) return false;
            for (int i = 1; i < lods.Length; i++)
                if (ListedIn(lods[i], renderer)) return true;
            return false;
        }

        private static bool ListedIn(LOD lod, Renderer renderer)
        {
            if (lod.renderers == null) return false;
            for (int i = 0; i < lod.renderers.Length; i++)
                if (lod.renderers[i] == renderer) return true;
            return false;
        }

        private static bool UnderNamed(Transform node, Transform root, string child)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
                if (t.name == child) return true;
            return false;
        }

        /// <summary>Same pose ItemStand uses: the item's attach marker, parented to the stand's attach point.</summary>
        private static void PoseOnItemStand(GameObject host, GameObject item, Transform holder)
        {
            Transform stand = host.GetComponent<ItemStand>()?.m_attachOther;
            if (stand == null)
            {
                foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                    if (t.name == "attach") { stand = t; break; }
            }
            if (stand == null) return;
            Transform grip = item.transform.Find("attach");
            holder.SetParent(stand, false);
            holder.localPosition = grip != null ? grip.localPosition : Vector3.zero;
            holder.localRotation = grip != null ? grip.localRotation : Quaternion.identity;
            holder.localScale = grip != null ? grip.localScale : Vector3.one;
        }

        private static void AddAura(Transform parent)
        {
            var lightObject = new GameObject("HaugbuiAura");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.color = new Color(0.55f, 0.08f, 1f);
            light.range = 5.5f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;
        }

        /// <summary>The Wraith's own smoke, shrunk onto the piece. One or two systems, never the whole creature.</summary>
        private static void CopyWraithMist(Transform parent)
        {
            GameObject wraith = PrefabManager.Cache.GetPrefab<GameObject>("Wraith");
            if (wraith == null)
            {
                Log.Warning("Wraith prefab not found; the altar has no mist.");
                return;
            }
            int copied = 0;
            ParticleSystem[] systems = wraith.GetComponentsInChildren<ParticleSystem>(true);
            copied = SpawnMist(systems, wraith, parent, namedOnly: true);
            if (copied == 0) copied = SpawnMist(systems, wraith, parent, namedOnly: false);
            if (copied == 0) Log.Warning("Wraith has no smoke particle to put on the altar.");
        }

        private static int SpawnMist(ParticleSystem[] systems, GameObject wraith, Transform parent, bool namedOnly)
        {
            int copied = 0;
            foreach (ParticleSystem system in systems)
            {
                if (system.transform == wraith.transform || system.GetComponent<ZNetView>() != null) continue;
                string path = system.transform.name.ToLowerInvariant();
                for (Transform t = system.transform.parent; t != null && t != wraith.transform; t = t.parent)
                    path = t.name.ToLowerInvariant() + "/" + path;
                bool named = path.IndexOf("smoke", System.StringComparison.Ordinal) >= 0
                    || path.IndexOf("mist", System.StringComparison.Ordinal) >= 0
                    || path.IndexOf("fog", System.StringComparison.Ordinal) >= 0
                    || path.IndexOf("aura", System.StringComparison.Ordinal) >= 0;
                bool attack = path.IndexOf("attack", System.StringComparison.Ordinal) >= 0
                    || path.IndexOf("hit", System.StringComparison.Ordinal) >= 0
                    || path.IndexOf("death", System.StringComparison.Ordinal) >= 0;
                if (namedOnly ? !named : !named && attack) continue;
                GameObject copy = UnityEngine.Object.Instantiate(system.gameObject, parent, false);
                copy.name = "HaugbuiMist";
                copy.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
                foreach (ZNetView view in copy.GetComponentsInChildren<ZNetView>(true)) UnityEngine.Object.Destroy(view);
                foreach (Transform child in copy.GetComponentsInChildren<Transform>(true))
                    child.gameObject.SetActive(true);
                foreach (ParticleSystem particles in copy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.EmissionModule emission = particles.emission;
                    emission.enabled = true;
                    ParticleSystem.MainModule main = particles.main;
                    main.playOnAwake = true;
                    particles.Play(true);
                }
                if (++copied >= 2) break;
            }
            return copied;
        }

        /// <summary>Drops the host colliders and puts a box around whatever is currently visible.</summary>
        public static void FitCollider(GameObject target)
        {
            bool any = false;
            Bounds box = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mesh.min.x : mesh.max.x, (i & 2) == 0 ? mesh.min.y : mesh.max.y, (i & 4) == 0 ? mesh.min.z : mesh.max.z);
                    Vector3 local = target.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { box = new Bounds(local, Vector3.zero); any = true; }
                    else box.Encapsulate(local);
                }
            }
            if (!any) return;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            BoxCollider fitted = target.AddComponent<BoxCollider>();
            fitted.center = box.center;
            fitted.size = box.size;
        }

        public static void Present(GameObject target, StructureDefinition definition)
        {
            if (Mathf.Abs(definition.Scale - 1f) > 0.01f)
                target.transform.localScale = Vector3.one * definition.Scale;
            if (definition.FitCollider)
                FitCollider(target);
            // The stand's own hover and attach prompt would hide the Haugbui piece. The attach transform stays.
            ItemStand stand = target.GetComponent<ItemStand>();
            if (stand != null)
                Object.DestroyImmediate(stand);
            UseShownFragments(target, definition);
        }

        /// <summary>
        /// The host's own debris is what flies apart. Point that at the meshes the player sees, and take the
        /// break effect from the same vanilla piece when it has one.
        /// </summary>
        private static void UseShownFragments(GameObject target, StructureDefinition definition)
        {
            if (definition.Kind == StructureKind.Longsword || definition.Kind == StructureKind.Tomb || StructureDefinitions.IsChest(definition.Kind))
                return;
            WearNTear wear = target.GetComponent<WearNTear>();
            Transform visual = target.transform.Find(RootName);
            if (wear == null || visual == null) return;
            wear.m_autoCreateFragments = true;
            // A hidden host still has active renderers that are merely disabled. The fragment pass does not check
            // enabled, so the whole object would throw the host's mesh too. The offering keeps its table.
            wear.m_fragmentRoots = definition.HideHost ? new[] { visual.gameObject } : new[] { target };
            foreach (VisualPart part in definition.Parts)
            {
                WearNTear source = FindPart(part.Prefab)?.GetComponent<WearNTear>();
                if (source == null || source.m_destroyedEffect == null) continue;
                wear.m_destroyedEffect = source.m_destroyedEffect;
                break;
            }
        }

        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");

        private static Material Tinted(Material source, Color? emission, Color? albedo, string diagnosticsKey)
        {
            if (source == null) return source;
            bool emissionOk = emission.HasValue && source.HasProperty(EmissionColor);
            bool albedoOk = albedo.HasValue && source.HasProperty(ColorProperty);
            if (!emissionOk && !albedoOk) return source;

            string key = source.GetInstanceID() + "|" + emission + "|" + albedo;
            if (TintCache.TryGetValue(key, out Material cached)) return cached;

            var copy = new Material(source) { name = source.name + " (Haugbui)" };
            string line = source.name + ": shader " + source.shader.name;
            if (emissionOk)
            {
                Color original = source.GetColor(EmissionColor);
                float intensity = Mathf.Max(original.r, original.g, original.b);
                copy.SetColor(EmissionColor, emission.Value * (intensity > 0f ? intensity : 1f));
                copy.EnableKeyword("_EMISSION");
                Texture map = source.HasProperty(EmissionMap) ? source.GetTexture(EmissionMap) : null;
                line += $", emission {original}, map {(map != null ? map.name : "none")}";
            }
            if (albedoOk)
            {
                copy.SetColor(ColorProperty, albedo.Value);
                line += $", albedo {albedo.Value}";
            }
            TintCache[key] = copy;
            MaterialDiagnostics[diagnosticsKey] = MaterialDiagnostics.TryGetValue(diagnosticsKey, out string previous)
                ? previous + "; " + line
                : line;
            return copy;
        }

        /// <summary>One light, and on Normal quality one particle system copied from the source prefab.</summary>
        private static void CopyGlow(string sourcePrefab, Transform parent, Color color)
        {
            var lightObject = new GameObject("HaugbuiLight");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.color = color;
            light.range = 2.2f;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;

            if (Creature.CreatureVisuals.LowQuality) return;
            GameObject source = PrefabManager.Cache.GetPrefab<GameObject>(sourcePrefab);
            if (source == null)
            {
                Log.Warning($"Glow source '{sourcePrefab}' not found.");
                return;
            }
            ParticleSystem particles = null;
            foreach (ParticleSystem system in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.GetComponent<ZNetView>() != null) continue;
                particles = system;
                break;
            }
            if (particles == null) return;
            GameObject copy = Object.Instantiate(particles.gameObject, parent, false);
            copy.name = "HaugbuiGlow";
            foreach (ZNetView view in copy.GetComponentsInChildren<ZNetView>(true)) Object.Destroy(view);
        }

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private static string PathOf(Transform node, Transform root)
        {
            var parts = new List<string>();
            for (Transform t = node; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return parts.Count == 0 ? "(root)" : string.Join("/", parts);
        }

        private static string HiddenReason(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return "inactive " + t.name;
            }
            return null;
        }
    }
}
