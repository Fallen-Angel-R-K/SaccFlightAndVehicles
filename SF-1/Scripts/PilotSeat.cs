
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PilotSeat : UdonSharpBehaviour
{
    public EngineController EngineControl;
    public GameObject LeaveButton;
    public GameObject Gun_pilot;
    public GameObject SeatAdjuster;
    private Transform PlaneMesh;
    private LayerMask Planelayer = 0;
    private ParticleSystem.CollisionModule gunpilotcol;
    private void Start()
    {
        Assert(EngineControl != null, "Start: EngineControl != null");
        Assert(LeaveButton != null, "Start: LeaveButton != null");
        Assert(Gun_pilot != null, "Start: Gun_pilot != null");
        Assert(SeatAdjuster != null, "Start: SeatAdjuster != null");

        PlaneMesh = EngineControl.PlaneMesh.transform;

        //make sure gun_pilot will never have collision enabled on the 'OnboardPlaneLayer'
        var GunPilotCollisionModule = Gun_pilot.GetComponent<ParticleSystem>().collision;
        int CollideLayers = GunPilotCollisionModule.collidesWith;
        CollideLayers = CollideLayers & (int.MaxValue ^ (1 << EngineControl.OnboardPlaneLayer));//NOT ~ is not implemented.
        GunPilotCollisionModule.collidesWith = CollideLayers;

        //get the layer of the plane as set by the world creator
        Planelayer = PlaneMesh.gameObject.layer;
    }
    private void Interact()//entering the plane
    {
        var Target = EngineControl.AAMTargets[EngineControl.AAMTarget];
        if (Target && Target.transform.parent)
        {
            EngineControl.AAMCurrentTargetEngineControl = Target.transform.parent.GetComponent<EngineController>();
        }

        if (EngineControl.VehicleMainObj != null) { Networking.SetOwner(EngineControl.localPlayer, EngineControl.VehicleMainObj); }
        if (LeaveButton != null) { LeaveButton.SetActive(true); }
        if (EngineControl != null)
        {
            EngineControl.VehicleRigidbody.angularDrag = 0;//set to something nonzero when you're not owner to prevent juddering motion on collisions
            if (!EngineControl.InEditor)
            {
                EngineControl.localPlayer.UseAttachedStation();
                Networking.SetOwner(EngineControl.localPlayer, EngineControl.gameObject);
            }
            EngineControl.Piloting = true;
            //canopy closed/open sound
            if (EngineControl.EffectsControl.CanopyOpen) EngineControl.CanopyCloseTimer = -100001;//has to be less than -100000
            else EngineControl.CanopyCloseTimer = -1;//less than 0
            if (EngineControl.dead) EngineControl.Health = 100;//dead is true for the first 5 seconds after spawn, this might help with spontaneous explosions
        }
        if (EngineControl.EffectsControl != null)
        {
            if (!EngineControl.InEditor)
                Networking.SetOwner(EngineControl.localPlayer, EngineControl.EffectsControl.gameObject);
            EngineControl.IsFiringGun = false;
            EngineControl.EffectsControl.Smoking = false;
            EngineControl.LGripLastFrame = false; //prevent instant flares drop on enter
        }
        if (EngineControl.HUDControl != null)
        {
            if (!EngineControl.InEditor)
                Networking.SetOwner(EngineControl.localPlayer, EngineControl.HUDControl.gameObject);
            EngineControl.HUDControl.gameObject.SetActive(true);
        }
        if (Gun_pilot != null) { Gun_pilot.SetActive(true); }
        if (SeatAdjuster != null) { SeatAdjuster.SetActive(true); }
        if (PlaneMesh != null)
        {
            Transform[] children = PlaneMesh.GetComponentsInChildren<Transform>();
            foreach (Transform child in children)
            {
                child.gameObject.layer = EngineControl.OnboardPlaneLayer;
            }
        }
    }
    public override void OnStationEntered(VRCPlayerApi player)
    {
        EngineControl.PilotName = player.displayName;
        EngineControl.Pilot = player;

        if (!player.isLocal)
            EngineControl.dead = false;//Plane stops being invincible if someone gets in, also acts as redundancy incase someone missed the notdead respawn event

        //old WakeUp();
        EngineControl.EffectsControl.DoEffects = 0f;
        EngineControl.SoundControl.DoSound = 0f;
        foreach (AudioSource thrust in EngineControl.SoundControl.Thrust)
        {
            thrust.gameObject.SetActive(true);
        }
        foreach (AudioSource idle in EngineControl.SoundControl.PlaneIdle)
        {
            idle.gameObject.SetActive(true);
        }
        if (!EngineControl.SoundControl.PlaneDistantNull) EngineControl.SoundControl.PlaneDistant.gameObject.SetActive(true);
        if (!EngineControl.SoundControl.PlaneWindNull) EngineControl.SoundControl.PlaneWind.gameObject.SetActive(true);
        if (!EngineControl.SoundControl.PlaneInsideNull) EngineControl.SoundControl.PlaneInside.gameObject.SetActive(true);
        if (EngineControl.SoundControl.soundsoff)
        {
            EngineControl.SoundControl.PlaneIdleVolume = 0;
            EngineControl.SoundControl.PlaneDistantVolume = 0;
            EngineControl.SoundControl.PlaneThrustVolume = 0;
        }
        EngineControl.SoundControl.soundsoff = false;
    }
    public override void OnStationExited(VRCPlayerApi player)
    {
        EngineControl.PilotName = string.Empty;
        EngineControl.Pilot = null;

        if (player.isLocal)
        {
            EngineControl.Piloting = false;
            if (EngineControl.Ejected)
            {
                EngineControl.localPlayer.SetVelocity(EngineControl.CurrentVel + EngineControl.VehicleMainObj.transform.up * 25);
                EngineControl.Ejected = false;
            }
            else EngineControl.localPlayer.SetVelocity(EngineControl.CurrentVel);
            EngineControl.EjectTimer = 2;
            EngineControl.Hooked = -1;
            EngineControl.BrakeInput = 0;
            EngineControl.LTriggerTapTime = 1;
            EngineControl.RTriggerTapTime = 1;
            EngineControl.Taxiinglerper = 0;
            EngineControl.PlayerThrottle = 0;
            EngineControl.LGripLastFrame = false;
            EngineControl.RGripLastFrame = false;
            EngineControl.LStickSelection = 0;
            EngineControl.RStickSelection = 0;
            EngineControl.BrakeInput = 0;
            EngineControl.LTriggerLastFrame = false;
            EngineControl.RTriggerLastFrame = false;
            EngineControl.HUDControl.MenuSoundCheckLast = 0;
            EngineControl.AGMLocked = false;
            EngineControl.AAMHasTarget = false;
            EngineControl.AAMLocked = false;
            EngineControl.MissilesIncoming = 0;
            EngineControl.EffectsControl.PlaneAnimator.SetInteger("missilesincoming", 0);
            EngineControl.AAMLockTimer = 0;
            EngineControl.AAMLocked = false;
            if (EngineControl.CatapultStatus == 1) { EngineControl.CatapultStatus = 0; }//keep launching if launching, otherwise unlock from catapult

            if (LeaveButton != null) { LeaveButton.SetActive(false); }
            if (EngineControl.EffectsControl != null)
            {
                EngineControl.IsFiringGun = false;
                EngineControl.EffectsControl.Smoking = false;
            }
            if (Gun_pilot != null) { Gun_pilot.SetActive(false); }
            if (SeatAdjuster != null) { SeatAdjuster.SetActive(false); }
            if (EngineControl.HUDControl != null) { EngineControl.HUDControl.gameObject.SetActive(false); }
            //set plane's layer back
            if (PlaneMesh != null)
            {
                Transform[] children = PlaneMesh.GetComponentsInChildren<Transform>();
                foreach (Transform child in children)
                {
                    child.gameObject.layer = Planelayer;
                }

            }
        }
    }
    private void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Debug.LogError("Assertion failed : '" + GetType() + " : " + message + "'", this);
        }
    }
}
