
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class HitDetector : UdonSharpBehaviour
{
    public EngineController EngineControl;
    public SoundController SoundControl;
    void OnParticleCollision(GameObject other)
    {
        if (other == null || EngineControl.dead) return;//avatars can't shoot you, and you can't get hurt when you're dead
        if (EngineControl.localPlayer == null)
        {
            PlaneHit();
        }
        else
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "PlaneHit");
        }
    }
    public void PlaneHit()
    {
        if (EngineControl.dead) return;
        if (EngineControl.localPlayer == null || EngineControl.localPlayer.IsOwner(EngineControl.gameObject))
        {
            EngineControl.Health -= 10;
        }
        if (EngineControl.EffectsControl != null) { EngineControl.EffectsControl.DoEffects = 0f; }
        if (SoundControl != null)
        {
            SoundControl.DoSound = 0f;
            if (SoundControl.BulletHit != null)
            {
                SoundControl.BulletHit.pitch = Random.Range(.8f, 1.2f);
                SoundControl.BulletHit.Play();
            }
        }
    }
    public void Respawn()//called by the explode animation on last frame
    {
        if (EngineControl.localPlayer == null)//editor
        {
            Respawn_event();
        }
        else if (EngineControl.localPlayer.IsOwner(EngineControl.VehicleMainObj))
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "Respawn_event");//owner broadcasts because it's more reliable than everyone doing it individually
        }
    }
    public void Respawn_event()//called by Respawn()
    {
        //re-enable plane model and effects
        EngineControl.EffectsControl.DoEffects = 6f; //wake up if was asleep
        EngineControl.EffectsControl.PlaneAnimator.SetTrigger("instantgeardown");
        if (EngineControl.localPlayer == null)//editor
        {
            EngineControl.VehicleRigidbody.velocity = new Vector3(0, 0, 0);
            EngineControl.VehicleMainObj.transform.rotation = Quaternion.Euler(EngineControl.EffectsControl.Spawnrotation);
            EngineControl.VehicleMainObj.transform.position = EngineControl.EffectsControl.Spawnposition;
            EngineControl.Health = EngineControl.FullHealth;
            EngineControl.GearUp = false;
            EngineControl.Flaps = true;
        }
        else if (EngineControl.localPlayer.IsOwner(EngineControl.VehicleMainObj))
        {
            EngineControl.Health = EngineControl.FullHealth;
            //this should respawn it in VRC, doesn't work in editor
            EngineControl.VehicleMainObj.transform.position = new Vector3(EngineControl.VehicleMainObj.transform.position.x, -10000, EngineControl.VehicleMainObj.transform.position.z);
            EngineControl.GearUp = false;
            EngineControl.Flaps = true;
        }
    }
    public void NotDead()//called by 'respawn' animation 5s in
    {
        if (EngineControl.localPlayer == null)//editor
        {
            NotDead_event();
        }
        else if (EngineControl.localPlayer.IsOwner(EngineControl.VehicleMainObj))
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "NotDead_event");//owner broadcasts because it's more reliable than everyone doing it individually
        }
    }
    public void NotDead_event()//called by NotDead()
    {
        if (EngineControl.localPlayer == null)//editor
        {
            EngineControl.Health = EngineControl.FullHealth;
            EngineControl.dead = false;
        }
        else if (EngineControl.localPlayer.IsOwner(EngineControl.VehicleMainObj))
        {
            EngineControl.Health = EngineControl.FullHealth;
        }
        EngineControl.dead = false;//because respawning gives us an immense number of Gs because we move so far in one frame, we stop being 'dead' 5 seconds after we respawn. Can't die when already dead. 
    }
}
