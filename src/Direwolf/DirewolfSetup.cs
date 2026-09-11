using System;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Rune.Direwolf
{
    public static class DirewolfSetup
    {
        public static void Configure(GameObject prefab)
        {
            var skin = DirewolfSkin.Load();
                var skeleton = skin.Apply(prefab);
                prefab.transform.localScale *= 1.2f;
                var body = prefab.GetComponent<Humanoid>();
                body.m_jumpForce = 8;
                body.m_jumpForceForward = 1.5f;
                body.m_jumpStaminaUsage = 0; // Jump cost is paid from the saddle's visible stamina pool.
                var ragdoll = PrefabManager.Instance.CreateClonedPrefab(prefab.name + "_Ragdoll", "Wolf_Ragdoll");
                if (!ragdoll) throw new InvalidOperationException("Native wolf ragdoll unavailable");
                skin.Apply(ragdoll);
                PrefabManager.Instance.AddPrefab(new CustomPrefab(ragdoll, false));
                var deathEffects = prefab.GetComponent<Humanoid>().m_deathEffects;
                var replacements = deathEffects.m_effectPrefabs.Select(effect =>
                {
                    var copy = new EffectList.EffectData();
                    foreach (var field in typeof(EffectList.EffectData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        field.SetValue(copy, field.GetValue(effect));
                    if (copy.m_prefab && copy.m_prefab.name == "Wolf_Ragdoll") copy.m_prefab = ragdoll;
                    return copy;
                }).ToArray();
                prefab.GetComponent<Humanoid>().m_deathEffects = new EffectList { m_effectPrefabs = replacements };
                var tame = prefab.GetComponent<Tameable>();
                var lox = PrefabManager.Instance.GetPrefab("Lox").GetComponent<Tameable>().m_saddle;
                if (!tame || !lox) throw new InvalidOperationException("Native taming/mount components unavailable");
                var mount = new GameObject("DirewolfSaddle");
                mount.SetActive(false);
                mount.layer = prefab.layer;
                var spine = skeleton.Find(skin.Paths.First(p => p.EndsWith("/Spine1", StringComparison.Ordinal)));
                mount.transform.SetParent(spine, false);
                mount.transform.position = skeleton.TransformPoint(new Vector3(0, .88f, .05f));
                mount.transform.rotation = skeleton.rotation;
                var saddle = mount.AddComponent<Sadle>();
                // Serialized settings only; never copy a live saddle's cached character or network state.
                foreach (var field in typeof(Sadle).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    field.SetValue(saddle, field.GetValue(lox));
                saddle.m_hoverText = "Direwolf saddle";
                saddle.m_mountIcon = DirewolfSkin.LoadMountIcon();
                var riderSeat = new GameObject("DirewolfRiderSeat");
                riderSeat.transform.SetParent(mount.transform, false);
                var seatFollow = riderSeat.AddComponent<DirewolfSeat>();
                seatFollow.Configure(skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh == skin.Mesh));
                saddle.m_attachPoint = riderSeat.transform;
                saddle.m_maxUseRange = 3;
                saddle.m_detachOffset = new Vector3(1.2f, .3f, 0);
                saddle.m_maxStamina = 150; saddle.m_runStaminaDrain = 10; saddle.m_swimStaminaDrain = 12;
                saddle.m_staminaRegen = 8; saddle.m_staminaRegenHungry = 2;
                var collider = mount.AddComponent<BoxCollider>();
                collider.size = new Vector3(.5f, .25f, .65f); collider.center = new Vector3(0, -.08f, 0);
                tame.m_saddle = saddle;
                // This generated creature has a permanently fitted saddle; there is no equip/drop item.
                tame.m_saddleItem = null; tame.m_dropSaddleOnDeath = false;
                prefab.AddComponent<DirewolfMount>();
                var reins = prefab.AddComponent<DirewolfReins>();
                reins.Configure(saddle, skeleton, skin.Paths.First(p => p.EndsWith("/Neck", StringComparison.Ordinal)));
        }
    }

    // Leather reins are visual tack; endpoints follow the native equipment grip transforms.
    internal sealed class DirewolfReins : MonoBehaviour
    {
        [SerializeField] private Sadle saddle;
        [SerializeField] private Transform leftAnchor, rightAnchor;
        private Player rider;
        private VisEquipment equipment;
        private LineRenderer leftLine, rightLine;
        private Material leather;

        internal void Configure(Sadle mount, Transform skeleton, string neckPath)
        {
            saddle=mount;
            leftAnchor=Anchor("Left rein",-.14f); rightAnchor=Anchor("Right rein",.14f);
            Transform Anchor(string name, float x)
            {
                var t=new GameObject(name).transform;t.SetParent(skeleton.Find(neckPath),false);
                t.position=skeleton.TransformPoint(new Vector3(x,.70f,.38f));return t;
            }
        }
        private LineRenderer CreateLine(string name)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>();line.sharedMaterial=leather;
            line.useWorldSpace=true;line.positionCount=5;line.startWidth=line.endWidth=.013f;
            line.numCornerVertices=2;line.numCapVertices=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            return line;
        }
        private void LateUpdate()
        {
            var player=Player.m_localPlayer;
            bool mounted=player && saddle && ReferenceEquals(player.GetDoodadController(),saddle) && player.IsAttached();
            if (!mounted) { Hide();rider=null;equipment=null;return; }
            if (rider!=player || !equipment) { rider=player;equipment=player.GetComponentInChildren<VisEquipment>(true); }
            if (!equipment || !equipment.m_leftHand || !equipment.m_rightHand) { Hide();return; }
            if (!leftLine)
            {
                var shader=Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                if (!shader) return;
                leather=new Material(shader) { name="Direwolf leather reins",color=new Color(.20f,.10f,.045f,1) };
                leftLine=CreateLine("Left leather rein");rightLine=CreateLine("Right leather rein");
            }
            Draw(leftLine,leftAnchor.position,equipment.m_leftHand.position);
            Draw(rightLine,rightAnchor.position,equipment.m_rightHand.position);
        }
        private static void Draw(LineRenderer line,Vector3 start,Vector3 grip)
        {
            line.enabled=true;
            for(int i=0;i<5;i++) { float t=i/4f;line.SetPosition(i,Vector3.Lerp(start,grip,t)+Vector3.down*(.10f*4*t*(1-t))); }
        }
        private void Hide() { if(leftLine) leftLine.enabled=false;if(rightLine) rightLine.enabled=false; }
        private void OnDisable() { Hide(); }
        private void OnDestroy() { if(leather) Destroy(leather); }
    }

    // Follow the same weighted surface as the visible saddle, rather than a different spine bone.
    internal sealed class DirewolfSeat : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer skin;
        private Transform[] bones;
        private Vector3[] bonePoints;
        private float[] weights;

        // Barycentric contact at skeleton-space x=0,z=.05 on the fitted saddle mesh.
        private static readonly int[] SeatVertices = { 8669, 9715, 7935 };
        private static readonly float[] SeatBlend = { .71687558f, .06494035f, .21818407f };
        internal void Configure(SkinnedMeshRenderer renderer) { skin=renderer; Cache(); Refresh(); }
        private void Awake() { if (skin) Cache(); }
        private void Cache()
        {
            var mesh=skin.sharedMesh;
            var vertices=mesh.vertices; var binding=mesh.bindposes; var skinWeights=mesh.boneWeights; var rig=skin.bones;
            bones=new Transform[12]; bonePoints=new Vector3[12]; weights=new float[12];
            for (int i=0;i<3;i++)
            {
                int vertex=SeatVertices[i]; var w=skinWeights[vertex];
                int[] joints={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};
                float[] influence={w.weight0,w.weight1,w.weight2,w.weight3};
                for (int k=0;k<4;k++)
                {
                    int slot=i*4+k; bones[slot]=rig[joints[k]];
                    bonePoints[slot]=binding[joints[k]].MultiplyPoint3x4(vertices[vertex]);
                    weights[slot]=SeatBlend[i]*influence[k];
                }
            }
        }
        internal void Refresh()
        {
            if (!skin) return;
            if (bones==null) Cache();
            var contact=Vector3.zero;
            for (int i=0;i<12;i++) contact+=bones[i].TransformPoint(bonePoints[i])*weights[i];
            transform.position=contact;
        }
        private void LateUpdate() { Refresh(); }
    }

    // Refresh before native attachment consumes the position, avoiding an extra frame of rider lag.
    [HarmonyPatch(typeof(Player), "UpdateAttach")]
    internal static class DirewolfSeatAttachment
    {
        private static void Prefix(Player __instance)
        {
            var point=__instance.GetAttachPoint();
            if (point && point.TryGetComponent<DirewolfSeat>(out var seat)) seat.Refresh();
        }
    }

    public sealed class DirewolfMount : MonoBehaviour
    {
        private ZNetView view;
        private Humanoid wolf;
        private Tameable tame;
        private MonsterAI ai;
        private bool riderAutoRun;
        private float attackAt;
        private float jumpAt;
        private static readonly Func<Sadle, bool> ValidUser = AccessTools.MethodDelegate<Func<Sadle, bool>>(AccessTools.Method(typeof(Sadle), "HaveValidUser"));
        public bool HasRider => tame && tame.m_saddle && tame.m_saddle.isActiveAndEnabled && ValidUser(tame.m_saddle);
        internal float Stamina => tame && tame.m_saddle ? GetStamina(tame.m_saddle) : 0;
        internal float MaxStamina => tame && tame.m_saddle ? tame.m_saddle.m_maxStamina : 0;
        public void DriveRider(float dt)
        {
            if (!HasRider) return;
            // Native Stop returns false and expects the normal AI tick to clear movement.
            // Rune bypasses that tick while ridden, so clear the previous steering here.
            if (!tame.m_saddle.UpdateRiding(dt)) { ai.StopMoving(); wolf.SetRun(false); }
        }
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));
        private static readonly Func<Sadle, bool> IsLocalUser = AccessTools.MethodDelegate<Func<Sadle, bool>>(AccessTools.Method(typeof(Sadle), "IsLocalUser"));
        private static readonly Func<Sadle, float, bool> HaveStamina = AccessTools.MethodDelegate<Func<Sadle, float, bool>>(AccessTools.Method(typeof(Sadle), "HaveStamina"));
        private static readonly Action<Sadle, float> UseStamina = AccessTools.MethodDelegate<Action<Sadle, float>>(AccessTools.Method(typeof(Sadle), "UseStamina"));
        private static readonly Func<Sadle, float> GetStamina = AccessTools.MethodDelegate<Func<Sadle, float>>(AccessTools.Method(typeof(Sadle), "GetStamina"));

        private void Start() { view=GetComponent<ZNetView>(); wolf=GetComponent<Humanoid>(); tame=GetComponent<Tameable>(); ai=GetComponent<MonsterAI>(); }
        private void Update()
        {
            if (!view || !view.IsValid() || !wolf || !tame || !tame.m_saddle) return;
            bool fitted = wolf.IsTamed();
            if (view.IsOwner() && view.GetZDO().GetBool(ZDOVars.s_haveSaddleHash, false) != fitted)
                view.GetZDO().Set(ZDOVars.s_haveSaddleHash, fitted);
            bool active = view.GetZDO().GetBool(ZDOVars.s_haveSaddleHash, false);
            if (tame.m_saddle.gameObject.activeSelf != active) tame.m_saddle.gameObject.SetActive(active);
            if (!HasRider) riderAutoRun=false;
        }
        internal void ApplyMovement(ref Vector3 moveDir, ref bool run, bool autoRun, ref bool block)
        {
            var player = Player.m_localPlayer;
            if (!player || !tame || !tame.m_saddle || !ReferenceEquals(player.GetDoodadController(), tame.m_saddle) || !IsLocalUser(tame.m_saddle)) return;
            riderAutoRun = MountRules.NextAutoRun(riderAutoRun, autoRun, moveDir.sqrMagnitude > .01f, block);
            if (riderAutoRun && moveDir.sqrMagnitude <= .01f) moveDir=Vector3.forward;
            if (MountRules.ShouldStop(moveDir.z, block))
            {
                // RPC_Controls ignores Turn while walking/running. Backward input sends Stop,
                // which the owner accepts at every speed; DriveRider then clears native movement.
                moveDir=new Vector3(0, 0, MountRules.StopInput);
                run=false;
                block=false;
            }
        }
        private bool CanControl(Player player) => view && view.IsValid() && view.IsOwner() && wolf && wolf.IsTamed() && !wolf.IsDead()
            && tame && tame.m_saddle && player && player == Player.m_localPlayer && ReferenceEquals(player.GetDoodadController(), tame.m_saddle)
            && IsLocalUser(tame.m_saddle) && TakeInput(player);
        internal void TryBite(Player player)
        {
            if (!CanControl(player)) return;
            if (Time.time < attackAt || wolf.InAttack() || wolf.IsDead() || !HaveStamina(tame.m_saddle, 10)) return;
            // Native attack checks and animation events produce hits; do not inject damage directly.
            if (wolf.StartAttack(null, false)) { UseStamina(tame.m_saddle, 10); attackAt=Time.time+1; }
        }
        internal void TryJump(Player player)
        {
            if (!CanControl(player)) return;
            bool blocked = wolf.InAttack() || wolf.InDodge() || wolf.IsStaggering() || wolf.IsKnockedBack() || wolf.IsEncumbered() || !wolf.CanMove();
            if (!MountRules.CanJump(true, wolf.IsOnGround(), wolf.IsSwimming(), blocked, GetStamina(tame.m_saddle), jumpAt-Time.time)) return;
            wolf.Jump(false); // Native grounded jump drives velocity, ground reset, effects and the wolf's jump animation.
            if (!wolf.IsOnGround()) { UseStamina(tame.m_saddle, MountRules.JumpCost); jumpAt=Time.time+.7f; }
        }
    }

    [HarmonyPatch(typeof(Sadle), nameof(Sadle.ApplyControlls))]
    internal static class DirewolfMovementInputs
    {
        private static void Prefix(Sadle __instance, ref Vector3 moveDir, ref bool run, bool autoRun, ref bool block)
        {
            var mount = __instance.GetComponentInParent<DirewolfMount>();
            if (mount) mount.ApplyMovement(ref moveDir, ref run, autoRun, ref block);
        }
    }

    // Native SetControls otherwise treats jump/attack as a request to leave the saddle.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class DirewolfRiderInputs
    {
        private static void Prefix(Player __instance, ref bool jump, ref bool attack, ref bool attackHold)
        {
            if (__instance != Player.m_localPlayer) return;
            var saddle = __instance.GetDoodadController() as Sadle;
            var mount = saddle ? saddle.GetComponentInParent<DirewolfMount>() : null;
            if (!mount) return;
            if (jump) { mount.TryJump(__instance); jump=false; }
            if (attack) { mount.TryBite(__instance); attack=false; }
            attackHold=false;
            // Secondary attack and dodge keep their native dismount behavior.
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.DropSaddle))]
    internal static class FixedSaddleDrop
    {
        private static bool Prefix(Tameable __instance, ref bool __result)
        {
            if (!__instance.GetComponent<DirewolfMount>()) return true;
            __result=false; return false;
        }
    }
    [HarmonyPatch(typeof(Sadle), nameof(Sadle.Interact))]
    internal static class FixedSaddleInteract
    {
        private static bool Prefix(Sadle __instance, bool alt, ref bool __result)
        {
            if (!alt || !__instance.GetComponentInParent<DirewolfMount>()) return true;
            __result=false; return false;
        }
    }
    [HarmonyPatch(typeof(Sadle), nameof(Sadle.GetHoverText))]
    internal static class FixedSaddleHover
    {
        private static void Postfix(Sadle __instance, ref string __result)
        {
            if (!__instance.GetComponentInParent<DirewolfMount>()) return;
            // Keep native distance text; remove only the unavailable saddle-removal line.
            var lines = __result.Split('\n');
            if (lines.Length > 2) __result = string.Join("\n", lines.Take(2));
        }
    }
}
