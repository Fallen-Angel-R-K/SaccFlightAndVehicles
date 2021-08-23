﻿
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class DFUNC_AAM : UdonSharpBehaviour
{
    [SerializeField] public EngineController EngineControl;
    [SerializeField] private Animator AAMAnimator;
    [SerializeField] private int NumAAM = 0;
    [SerializeField] private float Lock_Angle = 15;
    [SerializeField] private float AAMLockTime = 1.5f;
    [Tooltip("How long it takes to fully reload from 0 in seconds. Can be inaccurate because it can only reload by integers per resupply")]
    [SerializeField] private float FullReloadTimeSec = 10;
    private bool UseLeftTrigger = false;
    private int FullAAMs;
    private int NumAAMTargets;
    private float AAMLockAngle = 15;
    private float AAMLockTimer = 0;
    private bool AAMHasTarget = false;
    private bool AAMLocked = false;
    private bool TriggerLastFrame = false;
    private float AAMLastFiredTime = 0;
    private float AAMLaunchDelay = 0.5f;
    private int AAMS_STRING = Animator.StringToHash("AAMs");
    private float FullAAMsDivider;
    private int AAMLAUNCHED_STRING = Animator.StringToHash("aamlaunched");
    public GameObject AAM;
    public Transform AAMLaunchPoint;
    float TimeSinceSerialization;
    private bool func_active = false;
    public AudioSource AAMTargeting;
    public AudioSource AAMTargetLock;
    private float reloadspeed;
    private bool LeftDial = false;
    private int DialPosition = -999;
    public void DFUNC_LeftDial() { UseLeftTrigger = true; }
    public void DFUNC_RightDial() { UseLeftTrigger = false; }
    public void SFEXT_L_ECStart()
    {
        FullAAMs = NumAAM;
        reloadspeed = FullAAMs / FullReloadTimeSec;
        FullAAMsDivider = 1f / (NumAAM > 0 ? NumAAM : 10000000);
        NumAAMTargets = EngineControl.NumAAMTargets;
        AAMTargets = EngineControl.AAMTargets;
        CenterOfMass = EngineControl.CenterOfMass;
        VehicleTransform = EngineControl.VehicleTransform;
        OutsidePlaneLayer = EngineControl.PlaneMesh.gameObject.layer;

        //HUD
        if (HUDControl != null)
        {
            distance_from_head = (float)HUDControl.GetProgramVariable("distance_from_head");
        }
        if (distance_from_head == 0) { distance_from_head = 1.333f; }

        FindSelf();

        HUDText_AAM_ammo.text = NumAAM.ToString("F0");
    }
    public void SFEXT_O_PilotEnter()
    {
        HUDText_AAM_ammo.text = NumAAM.ToString("F0");
        //Make sure EngineControl.AAMCurrentTargetEngineControl is correct
        var Target = AAMTargets[AAMTarget];
        if (Target && Target.transform.parent)
        {
            AAMCurrentTargetEngineControl = Target.transform.parent.GetComponent<EngineController>();
        }
    }
    public void SFEXT_O_PilotExit()
    {
        TriggerLastFrame = false;
        RequestSerialization();
        gameObject.SetActive(false);
        AAMLockTimer = 0;
        AAMHasTarget = false;
        AAMLocked = false;
        func_active = false;
        AAMTargeting.gameObject.SetActive(false);
        AAMTargetLock.gameObject.SetActive(false);
        AAMTargetIndicator.localRotation = Quaternion.identity;
    }
    public void SFEXT_P_PassengerEnter()
    {
        HUDText_AAM_ammo.text = NumAAM.ToString("F0");
    }
    public void SFEXT_G_Explode()
    {
        NumAAM = FullAAMs;
        if (func_active)
        {
            DFUNC_Deselected();
        }
        else
        {
            DisableForOthers();
        }
    }
    public void SFEXT_G_ReSupply()
    {
        if (NumAAM != FullAAMs) { EngineControl.ReSupplied++; }
        NumAAM = (int)Mathf.Min(NumAAM + Mathf.Max(Mathf.Floor(reloadspeed), 1), FullAAMs);
        AAMAnimator.SetFloat(AAMS_STRING, (float)NumAAM * FullAAMsDivider);
        HUDText_AAM_ammo.text = NumAAM.ToString("F0");
    }
    public void SFEXT_G_RespawnButton()
    {
        NumAAM = FullAAMs;
        AAMAnimator.SetFloat(AAMS_STRING, 1);
    }
    public void SFEXT_G_TouchDown()
    {
        AAMLockTimer = 0;
        AAMTargetedTimer = 2;
    }
    public void DFUNC_Selected()
    {
        gameObject.SetActive(true);
        func_active = true;
        AAMTargetIndicator.gameObject.SetActive(true);
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "EnableForOthers");
    }
    public void DFUNC_Deselected()
    {
        AAMTargeting.gameObject.SetActive(false);
        AAMTargetLock.gameObject.SetActive(false);
        AAMLockTimer = 0;
        AAMHasTarget = false;
        AAMLocked = false;
        AAMTargetIndicator.localRotation = Quaternion.identity;
        AAMTargetIndicator.gameObject.SetActive(false);
        func_active = false;
        gameObject.SetActive(false);
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "DisableForOthers");
        TriggerLastFrame = false;
    }
    //synced variables recieved while object is disabled do not get set until the object is enabled
    public void EnableForOthers()
    {
        gameObject.SetActive(true);
    }
    public void DisableForOthers()
    {
        gameObject.SetActive(false);
    }
    void Update()
    {
        if (func_active)
        {
            TimeSinceSerialization += Time.deltaTime;
            float Trigger;
            if (UseLeftTrigger)
            { Trigger = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger"); }
            else
            { Trigger = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger"); }

            if (NumAAMTargets != 0)
            {

                if (AAMLockTimer > AAMLockTime && AAMHasTarget) AAMLocked = true;
                else { AAMLocked = false; }

                //firing AAM
                if (Trigger > 0.75 || (Input.GetKey(KeyCode.Space)))
                {
                    if (!TriggerLastFrame)
                    {
                        if (AAMLocked && !EngineControl.Taxiing && Time.time - AAMLastFiredTime > AAMLaunchDelay)
                        {
                            AAMLastFiredTime = Time.time;
                            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "LaunchAAM");
                            if (NumAAM == 0) { AAMLockTimer = 0; AAMLocked = false; }
                            if (EngineControl.IsOwner)
                            { EngineControl.SendEventToExtensions("SFEXT_O_AAMLaunch"); }
                        }
                    }
                    TriggerLastFrame = true;
                }
                else TriggerLastFrame = false;

                if (TimeSinceSerialization > .5f)
                {
                    TimeSinceSerialization = 0;
                    RequestSerialization();
                }
            }
            else { AAMLocked = false; AAMHasTarget = false; }


            //sound
            if (AAMLockTimer > 0 && !AAMLocked)
            {
                AAMTargeting.gameObject.SetActive(true);
                AAMTargetLock.gameObject.SetActive(false);
            }
            else if (AAMLocked)
            {
                AAMTargeting.gameObject.SetActive(false);
                AAMTargetLock.gameObject.SetActive(true);
            }
            else
            {
                AAMTargeting.gameObject.SetActive(false);
                AAMTargetLock.gameObject.SetActive(false);
            }
            Hud();
        }
    }

    //AAMTargeting
    [SerializeField] private UdonSharpBehaviour HUDControl;
    [SerializeField] private float AAMMaxTargetDistance = 6000;
    [System.NonSerializedAttribute] public GameObject[] AAMTargets;
    [System.NonSerializedAttribute] [UdonSynced(UdonSyncMode.None)] public int AAMTarget = 0;
    private int AAMTargetChecker = 0;
    private Transform CenterOfMass;
    private Transform VehicleTransform;
    private EngineController AAMCurrentTargetEngineControl;
    private int OutsidePlaneLayer;
    private Vector3 AAMCurrentTargetDirection;
    private float AAMTargetedTimer = 2;
    private float AAMTargetObscuredDelay;
    private void FixedUpdate()//old AAMTargeting function
    {
        if (func_active)
        {
            float DeltaTime = Time.fixedDeltaTime;
            var AAMCurrentTargetPosition = AAMTargets[AAMTarget].transform.position;
            Vector3 HudControlPosition = HUDControl.transform.position;
            float AAMCurrentTargetAngle = Vector3.Angle(VehicleTransform.forward, (AAMCurrentTargetPosition - HudControlPosition));

            //check 1 target per frame to see if it's infront of us and worthy of being our current target
            var TargetChecker = AAMTargets[AAMTargetChecker];
            var TargetCheckerTransform = TargetChecker.transform;
            var TargetCheckerParent = TargetCheckerTransform.parent;

            Vector3 AAMNextTargetDirection = (TargetCheckerTransform.position - HudControlPosition);
            float NextTargetAngle = Vector3.Angle(VehicleTransform.forward, AAMNextTargetDirection);
            float NextTargetDistance = Vector3.Distance(CenterOfMass.position, TargetCheckerTransform.position);
            bool AAMCurrentTargetEngineControlNull = AAMCurrentTargetEngineControl == null ? true : false;

            if (TargetChecker.activeInHierarchy)
            {
                EngineController NextTargetEngineControl = null;

                if (TargetCheckerParent)
                {
                    NextTargetEngineControl = TargetCheckerParent.GetComponent<EngineController>();
                }
                //if target EngineController is null then it's a dummy target (or hierarchy isn't set up properly)
                if ((!NextTargetEngineControl || (!NextTargetEngineControl.Taxiing && !NextTargetEngineControl.dead)))
                {
                    RaycastHit hitnext;
                    //raycast to check if it's behind something
                    bool LineOfSightNext = Physics.Raycast(HudControlPosition, AAMNextTargetDirection, out hitnext, 99999999, 133121 /* Default, Environment, and Walkthrough */, QueryTriggerInteraction.Ignore);

                    /*                 Debug.Log(string.Concat("LoS_next ", LineOfSightNext));
                                    if (hitnext.collider != null) Debug.Log(string.Concat("RayCastCorrectLayer_next ", (hitnext.collider.gameObject.layer == OutsidePlaneLayer)));
                                    if (hitnext.collider != null) Debug.Log(string.Concat("RayCastLayer_next ", hitnext.collider.gameObject.layer));
                                    Debug.Log(string.Concat("LowerAngle_next ", NextTargetAngle < AAMCurrentTargetAngle));
                                    Debug.Log(string.Concat("InAngle_next ", NextTargetAngle < 70));
                                    Debug.Log(string.Concat("BelowMaxDist_next ", NextTargetDistance < AAMMaxTargetDistance)); */

                    if ((LineOfSightNext
                        && hitnext.collider.gameObject.layer == OutsidePlaneLayer //did raycast hit an object on the layer planes are on?
                            && NextTargetAngle < Lock_Angle
                                && NextTargetAngle < AAMCurrentTargetAngle)
                                    && NextTargetDistance < AAMMaxTargetDistance
                                        || ((!AAMCurrentTargetEngineControlNull && AAMCurrentTargetEngineControl.Taxiing)//prevent being unable to switch target if it's angle is higher than your current target and your current target happens to be taxiing and is therefore untargetable
                                            || !AAMTargets[AAMTarget].activeInHierarchy))//same as above but if the target is destroyed
                    {
                        //found new target
                        AAMCurrentTargetAngle = NextTargetAngle;
                        AAMTarget = AAMTargetChecker;
                        AAMCurrentTargetPosition = AAMTargets[AAMTarget].transform.position;
                        AAMCurrentTargetEngineControl = NextTargetEngineControl;
                        AAMLockTimer = 0;
                        AAMTargetedTimer = .9f;//send targeted .1s after targeting so it can't get spammed too fast (and doesnt send if you instantly target something else)
                        AAMCurrentTargetEngineControlNull = AAMCurrentTargetEngineControl == null ? true : false;
                    }
                }
            }
            //increase target checker ready for next frame
            AAMTargetChecker++;
            if (AAMTargetChecker == AAMTarget && AAMTarget == NumAAMTargets - 1)
            { AAMTargetChecker = 0; }
            else if (AAMTargetChecker == AAMTarget)
            { AAMTargetChecker++; }
            else if (AAMTargetChecker > NumAAMTargets - 1)
            { AAMTargetChecker = 0; }

            //if target is currently in front of plane, lock onto it

            AAMCurrentTargetDirection = AAMCurrentTargetPosition - HudControlPosition;
            float AAMCurrentTargetDistance = AAMCurrentTargetDirection.magnitude;
            //check if target is active, and if it's enginecontroller is null(dummy target), or if it's not null(plane) make sure it's not taxiing or dead.
            //raycast to check if it's behind something
            RaycastHit hitcurrent;
            bool LineOfSightCur = Physics.Raycast(HudControlPosition, AAMCurrentTargetDirection, out hitcurrent, 99999999, 133121 /* Default, Environment, and Walkthrough */, QueryTriggerInteraction.Ignore);
            //used to make lock remain for .25 seconds after target is obscured
            if (LineOfSightCur == false || hitcurrent.collider.gameObject.layer != OutsidePlaneLayer)
            { AAMTargetObscuredDelay += DeltaTime; }
            else
            { AAMTargetObscuredDelay = 0; }

            if (!EngineControl.Taxiing
                && (AAMTargetObscuredDelay < .25f)
                    && AAMCurrentTargetDistance < AAMMaxTargetDistance
                        && AAMTargets[AAMTarget].activeInHierarchy
                            && (AAMCurrentTargetEngineControlNull || (!AAMCurrentTargetEngineControl.Taxiing && !AAMCurrentTargetEngineControl.dead)))
            {
                if ((AAMTargetObscuredDelay < .25f) && AAMCurrentTargetDistance < AAMMaxTargetDistance)
                {
                    AAMHasTarget = true;
                    if (AAMCurrentTargetAngle < Lock_Angle && NumAAM > 0)
                    {
                        AAMLockTimer += DeltaTime;
                        //give enemy radar lock even if you're out of missiles
                        if (!AAMCurrentTargetEngineControlNull)
                        {
                            //target is a plane, send the 'targeted' event every second to make the target plane play a warning sound in the cockpit.
                            AAMTargetedTimer += DeltaTime;
                            if (AAMTargetedTimer > 1)
                            {
                                AAMTargetedTimer = 0;
                                AAMCurrentTargetEngineControl.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetTargeted");
                            }
                        }
                    }
                    else
                    {
                        AAMTargetedTimer = 2f;
                        AAMLockTimer = 0;
                    }
                }
            }
            else
            {
                AAMTargetedTimer = 2f;
                AAMLockTimer = 0;
                AAMHasTarget = false;
            }
            /*         if (HUDControl.gameObject.activeInHierarchy)
                    {
                        Debug.Log(string.Concat("AAMTarget ", AAMTarget));
                        Debug.Log(string.Concat("HasTarget ", AAMHasTarget));
                        Debug.Log(string.Concat("AAMTargetObscuredDelay ", AAMTargetObscuredDelay));
                        Debug.Log(string.Concat("LoS ", LineOfSightCur));
                        Debug.Log(string.Concat("RayCastCorrectLayer ", (hitcurrent.collider.gameObject.layer == OutsidePlaneLayer)));
                        Debug.Log(string.Concat("RayCastLayer ", hitcurrent.collider.gameObject.layer));
                        Debug.Log(string.Concat("NotObscured ", AAMTargetObscuredDelay < .25f));
                        Debug.Log(string.Concat("InAngle ", AAMCurrentTargetAngle < Lock_Angle));
                        Debug.Log(string.Concat("BelowMaxDist ", AAMCurrentTargetDistance < AAMMaxTargetDistance));
                    } */
        }
    }



    //hud stuff
    [SerializeField] private Text HUDText_AAM_ammo;
    [SerializeField] private Transform AAMTargetIndicator;
    private float distance_from_head;
    private void Hud()
    {
        //AAM Target Indicator
        if (AAMHasTarget)//GUN or AAM
        {
            AAMTargetIndicator.localScale = Vector3.one;
            AAMTargetIndicator.position = HUDControl.transform.position + AAMCurrentTargetDirection;
            AAMTargetIndicator.localPosition = AAMTargetIndicator.localPosition.normalized * distance_from_head;
            if (AAMLocked)
            {
                AAMTargetIndicator.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));//back of mesh is locked version
            }
            else
            {
                AAMTargetIndicator.localRotation = Quaternion.identity;
            }
        }
        else AAMTargetIndicator.localScale = Vector3.zero;
        /////////////////
    }
    public void LaunchAAM()
    {
        if (NumAAM > 0) { NumAAM--; }//so it doesn't go below 0 when desync occurs
        AAMAnimator.SetTrigger(AAMLAUNCHED_STRING);
        if (AAM != null)
        {
            GameObject NewAAM = VRCInstantiate(AAM);
            if (!(NumAAM % 2 == 0))
            {
                //invert local x coordinates of launch point, launch, then revert
                Vector3 temp = AAMLaunchPoint.localPosition;
                temp.x *= -1;
                AAMLaunchPoint.localPosition = temp;
                NewAAM.transform.SetPositionAndRotation(AAMLaunchPoint.position, AAMLaunchPoint.transform.rotation);
                temp.x *= -1;
                AAMLaunchPoint.localPosition = temp;
            }
            else
            {
                NewAAM.transform.SetPositionAndRotation(AAMLaunchPoint.position, AAMLaunchPoint.transform.rotation);
            }
            NewAAM.SetActive(true);
            NewAAM.GetComponent<Rigidbody>().velocity = EngineControl.CurrentVel;
        }
        AAMAnimator.SetFloat(AAMS_STRING, (float)NumAAM * FullAAMsDivider);
        HUDText_AAM_ammo.text = NumAAM.ToString("F0");
    }
    private void FindSelf()
    {
        int x = 0;
        foreach (UdonSharpBehaviour usb in EngineControl.Dial_Functions_R)
        {
            if (this == usb)
            {
                DialPosition = x;
                return;
            }
            x++;
        }
        x = 0;
        foreach (UdonSharpBehaviour usb in EngineControl.Dial_Functions_L)
        {
            if (this == usb)
            {
                DialPosition = x;
                return;
            }
            x++;
        }
        DialPosition = -999;
        Debug.LogWarning("DFUNC_AAM: Can't find self in dial functions");
    }
    public void KeyboardInput()
    {
        if (LeftDial)
        {
            if (EngineControl.LStickSelection == DialPosition)
            { EngineControl.LStickSelection = -1; }
            else
            { EngineControl.LStickSelection = DialPosition; }
        }
        else
        {
            if (EngineControl.RStickSelection == DialPosition)
            { EngineControl.RStickSelection = -1; }
            else
            { EngineControl.RStickSelection = DialPosition; }
        }
    }
}
