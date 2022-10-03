
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace SaccFlightAndVehicles
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DefaultExecutionOrder(10)]
    public class SAV_SyncScript : UdonSharpBehaviour
    {
        // whispers to Zwei, "it's okay"
        public UdonSharpBehaviour SAVControl;
        [Tooltip("Delay between updates in seconds")]
        [Range(0.05f, 1f)]
        public float updateInterval = 0.2f;
        [Tooltip("Delay between updates in seconds when the sync has entered idle mode")]
        public float IdleModeUpdateInterval = 3f;
        [Tooltip("Freeze the vehicle's position when it's dead? Turn off for boats that sink etc")]
        public bool FreezePositionOnDeath = true;
        [Tooltip("How quickly to lerp rotation to new extrapolated target rotation, it might help to reduce this in high-lag situations with planes that can roll quickly")]
        public float RotationSyncAgressiveness = 10f;
        [Tooltip("Multiply velocity vectors received while in idle mode, useful for stopping sea vehicles from extrapolating above and below the water")]
        public float IdleModeVelMultiplier = .4f;
        [Tooltip("If vehicle moves less than this distance since it's last update, it'll be considered to be idle, may need to be increased for vehicles that want to be idle on water. If the vehicle floats away sometimes, this value is probably too big")]
        public float IdleMovementRange = .35f;
        [Tooltip("If vehicle rotates less than this many degrees since it's last update, it'll be considered to be idle")]
        public float IdleRotationRange = 5f;
        [Tooltip("Angle Difference between movement direction and rigidbody velocity that will cause the vehicle to teleport instead of interpolate")]
        public float TeleportAngleDifference = 20;
        [Tooltip("Maximum amount of extrapolation for high ping players, will brake formation flying for high ping players if set lower than their ping = 0.1 = 100ms, useful for dogfight worlds")]
        public float MaxPingExtrapolationInSeconds = 999f;
        [Tooltip("Set maximum extrapolation to 0 for passengers to reduce uncomfortable movement, passengers will not see formation flying properly.")]
        public bool PassengerComfortMode;
        [Tooltip("How much vehicle accelerates extra towards its 'raw' position when not owner in order to correct positional errors")]
        public float CorrectionTime = 8f;
        [Tooltip("How quickly non-owned vehicle's velocity vector lerps towards its new value")]
        public float SpeedLerpTime = 4f;
        [Tooltip("Strength of force to stop correction overshooting target")]
        public float CorrectionDerivStrength = 150f;
        [Tooltip("How much vehicle accelerates extra towards its 'raw' rotation when not owner in order to correct rotational errors")]
        public float CorrectionTime_Rotation = 1f;
        [Tooltip("How quickly non-owned vehicle's rotation slerps towards its new value")]
        public float RotationSpeedLerpTime = 10f;
        [Tooltip("Teleports owned vehicles forward by real time * velocity if frame takes too long to render and simulation slows down. Prevents other players from seeing you warp.")]
        public bool AntiWarp = true;
        [Header("DEBUG:")]
        [Tooltip("LEAVE THIS EMPTY UNLESS YOU WANT TO TEST THE NETCODE OFFLINE IN TEST MODE")]
        public Transform TestTransform;
        [Tooltip("UNCOMMENT THE CODE TO USE THIS. LEAVE THIS EMPTY UNLESS YOU WANT TO TEST THE NETCODE OFFLINE IN TEST MODE, If TestTransform is empty and this is filled you can see the raw position in multiplayer")]
        public Transform TestTransform_Raw;
        private Transform VehicleTransform;
        private double nextUpdateTime = float.MaxValue;
        private int StartupTimeMS = 0;
        private double dblStartupTimeMS = 0;
        [UdonSynced] private float O_UpdateTime;
        [UdonSynced] private Vector3 O_Position;
        [UdonSynced] private short O_RotationW;
        [UdonSynced] private short O_RotationX;
        [UdonSynced] private short O_RotationY;
        [UdonSynced] private short O_RotationZ;
        //sending velocity improves quality but will cause laggy movment if someone has very low fps.
        [UdonSynced] private Vector3 O_CurVel = Vector3.zero;
        private Vector3 O_CurVelLast = Vector3.zero;
        private Vector3 O_Rotation;
        private Quaternion O_Rotation_Q = Quaternion.identity;
        private Vector3 CurrentVelocityLast = Vector3.zero;
        private Quaternion CurAngMom = Quaternion.identity;
        private Quaternion CurAngMomAcceleration = Quaternion.identity;
        private Quaternion LastCurAngMom = Quaternion.identity;
        private Quaternion O_LastRotation = Quaternion.identity;
        // private Quaternion O_LastRotation2 = Quaternion.identity;
        private Quaternion RotationLerper = Quaternion.identity;
        private float Ping;
        // private float LastPing;
        private float L_UpdateTime;
        private float L_LastUpdateTime;
        private float O_LastUpdateTime;
        //make everyone think they're the owner for the first frame so that don't set the position to 0,0,0 before SFEXT_L_EntityStart runs
        private bool IsOwner = true;
        private Vector3 ExtrapolationDirection;
        private Quaternion RotationExtrapolationDirection;
        private Vector3 L_PingAdjustedPosition = Vector3.zero;
        private Quaternion L_PingAdjustedRotation = Quaternion.identity;
        private Vector3 L_LastPingAdjustedPosition;
        private Vector3 lerpedCurVel;
        private Vector3 Acceleration = Vector3.zero;
        private Vector3 LastAcceleration;
        private Vector3 O_LastPosition;
        private double UpdateTime;
        private int UpdatesSentWhileStill;
        private Rigidbody VehicleRigid;
        private bool Initialized = false;
        private bool IdleUpdateMode;
        private bool IdleUpdateMode_Last;
        private bool Piloting;
        private bool Occupied;
        private bool Grounded;
        private float CurrentUpdateInterval;
        private int EnterIdleModeNumber;
        private float PrevMaxExtrap;
        private double lastframetime;
        private Vector3 poslasframe;
        private Vector3 Extrapolation_Raw;
        private Quaternion RotExtrapolation_Raw;
        private double StartupServerTime;
        private Vector3 ExtrapDirection_Smooth;
        public Quaternion RotExtrapDirection_Smooth;
        // private Quaternion RotExtrapDirection_Smooth_Correction;
#if UNITY_EDITOR
        private bool TestMode;
#endif
        private float ErrorLastFrame;
        private void Start()
        {
            if (!VehicleRigid)
            {
                VehicleRigid = ((SaccEntity)SAVControl.GetProgramVariable("EntityControl")).GetComponent<Rigidbody>();
            }
            if (!TestTransform)
            { TestTransform = VehicleRigid.transform; }
#if UNITY_EDITOR
            else { TestMode = true; }
#endif
            RotExtrapolation_Raw = Quaternion.identity;
        }
        public void SFEXT_L_EntityStart()
        {
            Initialized = true;
            VehicleTransform = ((SaccEntity)SAVControl.GetProgramVariable("EntityControl")).transform;
            VehicleRigid = (Rigidbody)SAVControl.GetProgramVariable("VehicleRigidbody");
            Extrapolation_Raw = L_LastPingAdjustedPosition = L_PingAdjustedPosition = O_Position = VehicleTransform.position;
            /* O_LastRotation2 = */
            O_LastRotation = O_Rotation_Q = VehicleTransform.rotation;
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            bool InEditor = localPlayer == null;
            if (!InEditor)
            {
                if (localPlayer.isMaster)
                {
                    IsOwner = true;
                    VehicleRigid.drag = 0;
                    VehicleRigid.angularDrag = 0;
                }
                else
                {
                    IsOwner = false;
                    VehicleRigid.drag = 9999;
                    VehicleRigid.angularDrag = 9999;
                }
            }
            else
            {//play mode in editor
                IsOwner = true;
                VehicleRigid.drag = 9999;
                VehicleRigid.angularDrag = 9999;
            }
            StartupServerTime = Networking.GetServerTimeInSeconds();

            CurrentUpdateInterval = updateInterval;
            EnterIdleModeNumber = Mathf.FloorToInt(IdleModeUpdateInterval / updateInterval);//enter idle after IdleModeUpdateInterval seconds of being still
                                                                                            //script is disabled for 5 seconds to make sure nothing moves before everything is initialized
            SendCustomEventDelayedSeconds(nameof(ActivateScript), 5);
        }
        public void ActivateScript()
        {
            gameObject.SetActive(true);
            VehicleRigid.constraints = RigidbodyConstraints.None;
            if (IsOwner)
            {
                VehicleRigid.isKinematic = false;
                VehicleRigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            else
            {
                VehicleRigid.isKinematic = true;
                VehicleRigid.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            nextUpdateTime = (float)(Networking.GetServerTimeInSeconds() - StartupServerTime) + Random.Range(0f, updateInterval);
        }
        public void SFEXT_O_TakeOwnership()
        {
            lastframetime = (Networking.GetServerTimeInSeconds() - StartupServerTime);
            IsOwner = true;
            VehicleRigid.isKinematic = false;
            VehicleRigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            VehicleRigid.drag = 0;
            VehicleRigid.angularDrag = 0;
        }
        public void SFEXT_O_LoseOwnership()
        {
            IsOwner = false;
            Extrapolation_Raw = L_LastPingAdjustedPosition = L_PingAdjustedPosition = O_Position;
            ExtrapDirection_Smooth = CurrentVelocityLast; ;
            RotationLerper = /* O_LastRotation2 = */ O_LastRotation = O_Rotation_Q;
            LastCurAngMom = CurAngMom = Quaternion.identity;
            VehicleRigid.isKinematic = true;
            VehicleRigid.collisionDetectionMode = CollisionDetectionMode.Continuous;
            VehicleRigid.drag = 9999;
            VehicleRigid.angularDrag = 9999;
            UpdatesSentWhileStill = 0;
            IdleUpdateMode_Last = false;
        }
        public void SFEXT_O_PilotEnter()
        {
            Piloting = true;
            if (IdleUpdateMode) { nextUpdateTime = 0; }
        }
        public void SFEXT_G_PilotEnter()
        {
            if (IdleUpdateMode)
            { ExitIdleMode(); }
            Occupied = true;
        }
        public void SFEXT_G_PilotExit()
        { Occupied = false; }
        public void SFEXT_G_TakeOff()
        {
            if (IdleUpdateMode) { ExitIdleMode(); }
            Grounded = false;
        }
        public void SFEXT_L_OwnershipTransfer()
        { if (IdleUpdateMode) { ExitIdleMode(); } }
        public void SFEXT_O_PilotExit()
        { Piloting = false; }
        public void SFEXT_O_RespawnButton()
        {
            nextUpdateTime = 0;
        }
        public void SFEXT_G_RespawnButton()
        {
            ExitIdleMode();
            UpdatesSentWhileStill = 0;
            //make it teleport instead of interpolating
            ExtrapolationDirection = Vector3.zero;
            Extrapolation_Raw = VehicleTransform.position = L_LastPingAdjustedPosition = L_PingAdjustedPosition = O_LastPosition = O_Position;
            RotationLerper = VehicleTransform.rotation = /* O_LastRotation2 = */ O_LastRotation = O_Rotation_Q;
            ExtrapDirection_Smooth = Vector3.zero;
            CurrentVelocityLast = Vector3.zero;
            LastAcceleration = Acceleration = Vector3.zero;
        }
        private void Update()
        {
            if (IsOwner)//send data
            {
                double time = (Networking.GetServerTimeInSeconds() - StartupServerTime);
                if (AntiWarp && Time.deltaTime > .083f)//let's see if we can fix the physics jerkiness for observers if the FPS is extremely low
                {
                    double acctime = (Networking.GetServerTimeInSeconds() - StartupServerTime);
                    double accuratedelta = acctime - lastframetime;
                    Vector3 RigidMovedAmount = VehicleRigid.velocity * Time.deltaTime;
                    float DistanceTravelled = RigidMovedAmount.magnitude;

                    if (DistanceTravelled < (VehicleRigid.velocity * (float)accuratedelta).magnitude)
                    {
                        //smooth, but the extrapolation gets added each time (i think) causing vehicle to be faster (10%~)
                        //VehicleTransform.position += (VehicleRigid.velocity * (float)accuratedelta) - RigidMovedAmount;
                        //it's more correct to use RB position, but then you're removing the RB extrapolation and things get jerky.
                        //When setting rigidbody position, although it looks more jerky when flying side-by-side, it's more accurate speed-wise
                        //and hopefully doesn't cause rapid speed-up-slow-down if you keep on transitioning in and out of the parent if statement.
                        //Setting transform position to rigidbody position+, so that position is correct if data is sent this frame (the result should be the jerky, speed-accurate one)
                        VehicleTransform.position = VehicleRigid.position + (VehicleRigid.velocity * (float)accuratedelta) - RigidMovedAmount;
                        //is there a best of both worlds solution?
                    }
                }
                lastframetime = time;
                if (time > nextUpdateTime - (Time.deltaTime * .5f))
                {
                    if (!Networking.IsClogged || Piloting)
                    {
                        //check if the vehicle has moved enough from it's last sent location and rotation to bother exiting idle mode
                        bool Still = !Piloting && Grounded && (((VehicleTransform.position - O_Position).magnitude < IdleMovementRange) && Quaternion.Angle(VehicleTransform.rotation, O_Rotation_Q) < IdleRotationRange);

                        if (Still)
                        {
                            UpdatesSentWhileStill++;
                            if (UpdatesSentWhileStill > EnterIdleModeNumber)
                            { IdleUpdateMode = true; }
                        }
                        else
                        {
                            UpdatesSentWhileStill = 0;
                            IdleUpdateMode = false;
                        }
                        if (IdleUpdateMode)
                        {
                            if (!IdleUpdateMode_Last)
                            { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(EnterIdleMode)); }
                        }
                        else
                        {
                            if (IdleUpdateMode_Last)
                            { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ExitIdleMode)); }
                        }
                        IdleUpdateMode_Last = IdleUpdateMode;
                        //never use rigidbody values for position or rotation because the interpolation/extrapolation from update is needed for it to be smooth
                        O_Position = VehicleTransform.position;
                        O_Rotation_Q = VehicleTransform.rotation;
                        //convert each euler angle to shorts to save bandwidth
                        float smv = short.MaxValue;
                        O_RotationX = (short)(O_Rotation_Q.x * smv);
                        O_RotationY = (short)(O_Rotation_Q.y * smv);
                        O_RotationZ = (short)(O_Rotation_Q.z * smv);
                        O_RotationW = (short)(O_Rotation_Q.w * smv);

                        O_CurVel = VehicleRigid.velocity;

                        O_UpdateTime = (float)((dblStartupTimeMS) + (time));//send servertime of update
                        RequestSerialization();
                        UpdateTime = time;
                    }
                    nextUpdateTime = time + (IdleUpdateMode ? IdleModeUpdateInterval : updateInterval);
                }
#if UNITY_EDITOR
                if (TestMode)
                {
                    ExtrapolationAndSmoothing();
                }
#endif
            }
            else//extrapolate and interpolate based on received data
            {
                ExtrapolationAndSmoothing();
            }
        }
        private void ExtrapolationAndSmoothing()
        {
            if (Deserialized)
            {
                Deserialized = false;
                DeserializationStuff();
            }
            float deltatime = Time.deltaTime;
            float TimeSinceUpdate = ((float)((dblStartupTimeMS + ((Networking.GetServerTimeInSeconds() - StartupServerTime))) - L_UpdateTime))
                    / updateInterval;
            //extrapolated position based on time passed since update
            Vector3 Correction = ((Extrapolation_Raw - TestTransform.position) * CorrectionTime);
            Vector3 VelEstimate = O_CurVel + (Acceleration * TimeSinceUpdate);

            //like a PID derivative. Makes movement a bit jerky because the 'raw' target is jerky.
            float Error = Vector3.Distance(TestTransform.position, Extrapolation_Raw);
            Vector3 Deriv = Vector3.zero;
            if (ErrorLastFrame > Error)
            {
                Deriv = -Correction.normalized * (ErrorLastFrame - Error) * CorrectionDerivStrength;
            }
            ErrorLastFrame = Error;
            ExtrapDirection_Smooth = Vector3.Lerp(ExtrapDirection_Smooth, VelEstimate + Correction + Deriv, SpeedLerpTime * deltatime);

            //rotate using method to movement (no deriv, correction is done with a simple slerp later)
            Quaternion FrameRotAccel = RealSlerp(Quaternion.identity, CurAngMomAcceleration, TimeSinceUpdate);
            Quaternion AngMomEstimate = FrameRotAccel * CurAngMom;
            RotExtrapDirection_Smooth = RealSlerp(RotExtrapDirection_Smooth, AngMomEstimate, RotationSpeedLerpTime * deltatime);

            //apply positional update
            Extrapolation_Raw += ExtrapolationDirection * deltatime;
            Quaternion FrameRotExtrap = RealSlerp(Quaternion.identity, RotationExtrapolationDirection, deltatime);
            RotExtrapolation_Raw = FrameRotExtrap * RotExtrapolation_Raw;
            TestTransform.position += ExtrapDirection_Smooth * deltatime;
            //apply rotational update
            Quaternion FrameRotExtrap_Smooth = RealSlerp(Quaternion.identity, RotExtrapDirection_Smooth, deltatime);
            TestTransform.rotation = FrameRotExtrap_Smooth * TestTransform.rotation;
            //correct rotational desync
            TestTransform.rotation = RealSlerp(TestTransform.rotation, RotExtrapolation_Raw, CorrectionTime_Rotation * deltatime);
#if UNITY_EDITOR
            if (TestTransform_Raw)
            {
                TestTransform_Raw.position = Extrapolation_Raw;
                TestTransform_Raw.rotation = RotExtrapolation_Raw;
            }
#endif
        }
        public void EnterIdleMode()
        {
            if (IdleUpdateMode || Occupied) { return; }
            IdleUpdateMode = true;
            CurrentUpdateInterval = IdleModeUpdateInterval;
        }
        public void ExitIdleMode()
        {
            if (!IdleUpdateMode) { return; }
            IdleUpdateMode = false;
            CurrentUpdateInterval = updateInterval;
        }
        public void SFEXT_O_OnPlayerJoined()
        {
            if (IdleUpdateMode)
            { SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(EnterIdleMode)); }
        }
        public override void OnDeserialization()
        {
            if (!IsOwner)//only do anything if OnDeserialization was for this script
            {
                DeserializationCheck();
            }
        }
#if UNITY_EDITOR
        public float LagSimDelay;
        private float LagSimTime;
        private bool LagSimWait;
        public float DBGPING;
        private void FixedUpdate()
        {
            if (TestMode)
            { DeserializationCheck(); }
        }
#endif
        private bool Deserialized = false;
        private void DeserializationCheck()
        {
            if (O_UpdateTime != O_LastUpdateTime)//only do anything if OnDeserialization was for this script
            {
#if UNITY_EDITOR
                if (!LagSimWait)
                {
                    if (LagSimDelay != 0)
                    {
                        LagSimWait = true;
                        LagSimTime = Time.time;
                        return;
                    }
                }
                else
                {
                    if (Time.time - LagSimTime > LagSimDelay)
                    {
                        LagSimWait = false;
                    }
                    else
                    {
                        return;
                    }
                }
#endif
                Deserialized = true;
            }
        }
        public void DeserializationStuff()
        {
            LastAcceleration = Acceleration;
            // LastPing = Ping;
            L_LastUpdateTime = L_UpdateTime;
            LastCurAngMom = CurAngMom;
            //time between this update and last
            float updatedelta = (float)(O_UpdateTime - O_LastUpdateTime);
            float speednormalizer = 1 / updatedelta;

            //local time update was received
            L_UpdateTime = (float)((dblStartupTimeMS) + ((Networking.GetServerTimeInSeconds() - StartupServerTime)));
            //Ping is time between server time update was sent, and the local time the update was received
            Ping = Mathf.Min((float)(L_UpdateTime - O_UpdateTime), MaxPingExtrapolationInSeconds);
#if UNITY_EDITOR
            DBGPING = Ping;
#endif
            //Curvel is 0 when launching from a catapult because it doesn't use rigidbody physics, so do it based on position
            Vector3 CurrentVelocity;
            bool SetVelZero = false;
            if (O_CurVel.sqrMagnitude == 0)
            {
                if (O_CurVelLast.sqrMagnitude != 0)
                { CurrentVelocity = Vector3.zero; SetVelZero = true; }
                else
                { CurrentVelocity = (O_Position - O_LastPosition) * speednormalizer; }
            }
            else
            { CurrentVelocity = O_CurVel; }
            O_CurVelLast = O_CurVel;
            //if direction of acceleration changed by more than 90 degrees, just set zero to prevent bounce effect, the vehicle likely just crashed into a wall.
            Acceleration = (CurrentVelocity - CurrentVelocityLast);//acceleration is difference in velocity
            if (IdleUpdateMode || Vector3.Dot(Acceleration, LastAcceleration) < 0 || SetVelZero)
            { Acceleration = Vector3.zero; }

            float smv = short.MaxValue;
            O_Rotation_Q = (new Quaternion(O_RotationX / smv, O_RotationY / smv, O_RotationZ / smv, O_RotationW / smv));

            //rotate Acceleration by the difference in rotation of vehicle between last and this update to make it match the angle for the next update better
            Quaternion PlaneRotDif = O_Rotation_Q * Quaternion.Inverse(O_LastRotation);
            Acceleration = (PlaneRotDif * Acceleration) * .5f;//not sure why it's 0.5, but it seems correct from testing
            Acceleration += Acceleration * (Ping / updateInterval);

            //current angular momentum as a quaternion
            CurAngMom = RealSlerp(Quaternion.identity, PlaneRotDif, speednormalizer);
            CurAngMomAcceleration = CurAngMom * Quaternion.Inverse(LastCurAngMom);

            RotationExtrapolationDirection = CurAngMomAcceleration * CurAngMom;
            Quaternion PingRotExtrap = RealSlerp(Quaternion.identity, RotationExtrapolationDirection, Ping);
            L_PingAdjustedRotation = PingRotExtrap * O_Rotation_Q;
            Quaternion FrameRotExtrap = RealSlerp(Quaternion.identity, RotationExtrapolationDirection, -Time.deltaTime);
            RotExtrapolation_Raw = FrameRotExtrap * L_PingAdjustedRotation;//undo 1 frame worth of movement because its done again in update()

            //tell the SaccAirVehicle the velocity value because it doesn't sync it itself
            SAVControl.SetProgramVariable("CurrentVel", CurrentVelocity);
            L_PingAdjustedPosition = O_Position + ((CurrentVelocity + (Acceleration)) * Ping);
            ExtrapolationDirection = CurrentVelocity + (Acceleration);

            Extrapolation_Raw = L_PingAdjustedPosition - (ExtrapolationDirection * Time.deltaTime);//undo 1 frame worth of movement because its done again in update()
            if (IdleUpdateMode) { ExtrapolationDirection *= IdleModeVelMultiplier; }
            // O_LastRotation2 = O_LastRotation;//O_LastRotation2 is needed for use in Update() as O_LastRotation is the same as O_Rotation_Q there

            O_LastUpdateTime = O_UpdateTime;
            O_LastRotation = O_Rotation_Q;
            O_LastPosition = O_Position;
            CurrentVelocityLast = CurrentVelocity;

            //float MoveDot = Vector3.Dot(Movement, O_CurVel);
            //if we're going one way but moved the other, we must have teleported.
            //set values to the same thing for Current and Last to make teleportation instead of interpolation
            if (Vector3.Angle(O_Position - O_LastPosition, O_CurVel) > TeleportAngleDifference
            //|| MoveDot > 2 || MoveDot < 0//also teleport if we moved way faster than we should have //disabled because it probably makes very slow framerate people teleport all the time
            )
            {
                L_LastUpdateTime = L_UpdateTime;
                L_LastPingAdjustedPosition = L_PingAdjustedPosition;
                LastCurAngMom = CurAngMom;
                // LastPing = Ping;
                // O_LastRotation2 = O_LastRotation = O_Rotation_Q;
                O_LastPosition = O_Position;
                TestTransform.position = Extrapolation_Raw;
            }
        }
        public void SFEXT_O_Explode()//all the things players see happen when the vehicle explodes
        {
            if (IsOwner && FreezePositionOnDeath)
            {
                VehicleRigid.drag = 9999;
                VehicleRigid.angularDrag = 9999;
            }
        }
        public void SFEXT_G_ReAppear()
        {
            if (IsOwner)
            {
                VehicleRigid.drag = 0;
                VehicleRigid.angularDrag = 0;
            }
        }
        public void SFEXT_O_MoveToSpawn()
        {
            if (IsOwner)
            {
                VehicleRigid.drag = 9999;
                VehicleRigid.angularDrag = 9999;
            }
        }
        public void SFEXT_P_PassengerEnter()
        {
            if (PassengerComfortMode)
            {
                PrevMaxExtrap = MaxPingExtrapolationInSeconds;
                MaxPingExtrapolationInSeconds = 0;
            }
        }
        public void SFEXT_P_PassengerExit()
        {
            if (PassengerComfortMode)
            {
                MaxPingExtrapolationInSeconds = PrevMaxExtrap;
            }
        }
        public void SFEXT_G_TouchDown()
        {
            Grounded = true;
        }
        public void SFEXT_G_TouchDownWater()
        {
            Grounded = true;
        }
        //unity slerp always uses shortest route to orientation rather than slerping to the actual quat. This undoes that
        public Quaternion RealSlerp(Quaternion p, Quaternion q, float t)
        {
            if (Quaternion.Dot(p, q) < 0)
            {
                float angle = Quaternion.Angle(p, q);//quaternion.angle also checks shortest route
                float newvalue = (360f - angle) / angle;
                return Quaternion.SlerpUnclamped(p, q, -t * newvalue);
            }
            else return Quaternion.SlerpUnclamped(p, q, t);
        }
    }
}