using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MLAgents;
public class AssaultCourse004WalkerAgent : MarathonAgent {

    AssaultCourse004TerrainAgent _assaultCourse004TerrainAgent;
    int _lastXPosInMeters;
    float _pain;
    bool _modeRecover;
    Vector3 _centerOfMass;

    public override void AgentReset()
    {
        base.AgentReset();

        BodyParts["pelvis"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="torso");
        BodyParts["left_thigh"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="left_thigh");
        BodyParts["right_thigh"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="right_thigh");
        BodyParts["left_leg"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="left_leg");
        BodyParts["right_leg"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="right_leg");
        BodyParts["left_foot"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="left_foot");
        BodyParts["right_foot"] = GetComponentsInChildren<Rigidbody>().FirstOrDefault(x=>x.name=="right_foot");

        SetCenterOfMass();

        if (_assaultCourse004TerrainAgent == null)
            _assaultCourse004TerrainAgent = GetComponent<AssaultCourse004TerrainAgent>();
        _lastXPosInMeters = (int) ((BodyParts["left_foot"].transform.position.x + BodyParts["right_foot"].transform.position.x)/2);
        _assaultCourse004TerrainAgent.Terminate(GetCumulativeReward());

        // set to true this to show monitor while training
        Monitor.SetActive(true);

        StepRewardFunction = StepRewardHopper101;
        TerminateFunction = LocalTerminate;
        ObservationsFunction = ObservationsDefault;
        // OnTerminateRewardValue = -100f;
        _pain = 0f;
        _modeRecover = false;

        base.SetupBodyParts();
        SetCenterOfMass();
    }

    bool LocalTerminate()
    {
        int newXPosInMeters = (int) ((BodyParts["left_foot"].transform.position.x + BodyParts["right_foot"].transform.position.x)/2);;
        if (newXPosInMeters > _lastXPosInMeters) {
            _assaultCourse004TerrainAgent.OnNextMeter();
            _lastXPosInMeters = newXPosInMeters;
        }

        SetCenterOfMass();
        var xpos = ((BodyParts["left_foot"].transform.position.x + BodyParts["right_foot"].transform.position.x)/2);;
        var terminate = false;
        if (xpos < 4f && _pain > 1f)
            terminate = true;
        else if (xpos < 2f && _pain > 0f)
            terminate = true;
        if (terminate)
            _assaultCourse004TerrainAgent.Terminate(GetCumulativeReward());

        return terminate;
    }
    public override void OnTerrainCollision(GameObject other, GameObject terrain) {
        if (string.Compare(terrain.name, "Terrain", true) != 0)
            return;

        switch (other.name.ToLowerInvariant().Trim())
        {
            case "left_thigh": 
            case "right_thigh": 
            case "torso": 
                _pain += 5f;
                NonFootHitTerrain = true;
                _modeRecover = true;
                break;
            case "left_foot":
            case "right_foot":
            case "left_leg":
            case "right_leg":
                FootHitTerrain = true;
                break;
            default:
                _pain += 5f;
                NonFootHitTerrain = true;
                _modeRecover = true;
                break;
        }
    }


    public override void AgentOnDone()
    {
    }    
    void ObservationsDefault()
    {
        if (ShowMonitor) {
        }
        var pelvis = BodyParts["pelvis"];
        AddVectorObs(pelvis.velocity);
        AddVectorObs(pelvis.transform.forward); // gyroscope 
        AddVectorObs(pelvis.transform.up);
        
        AddVectorObs(SensorIsInTouch);
        JointRotations.ForEach(x=>AddVectorObs(x));
        AddVectorObs(JointVelocity);
        AddVectorObs(BodyParts["left_foot"].transform.position.y / AssaultCourse004TerrainAgent._maxHeight);
        AddVectorObs(BodyParts["right_foot"].transform.position.y / AssaultCourse004TerrainAgent._maxHeight);

        var xpos = pelvis.transform.position.x;
        xpos -= 2f;
        float fraction = (xpos - (Mathf.Floor(xpos*5)/5)) * 5;
        float ypos = pelvis.transform.position.y;
        List<Ray> rays = Enumerable.Range(0, 5*5).Select(x => new Ray(new Vector3(xpos+(x*.2f), AssaultCourse004TerrainAgent._maxHeight, 0f), Vector3.down)).ToList();
        List<float> distances = rays.Select
            ( x=>
                ypos - (AssaultCourse004TerrainAgent._maxHeight - 
                Physics.RaycastAll(x)
                .OrderBy(y=>y.distance)
                .FirstOrDefault()
                .distance)
            ).ToList();
        if (Application.isEditor && ShowMonitor)
        {
            var view = distances.Skip(10).Take(20).Select(x=>x).ToList();
            Monitor.Log("distances", view.ToArray());
            var time = Time.deltaTime;
            time *= agentParameters.numberOfActionsBetweenDecisions;
            for (int i = 0; i < rays.Count; i++)
            {
                var distance = distances[i];
                var origin = new Vector3(rays[i].origin.x, ypos,0f);
                var direction = distance > 0 ? Vector3.down : Vector3.up;
                var color = distance > 0 ? Color.yellow : Color.red;
                Debug.DrawRay(origin, direction*Mathf.Abs(distance), color, time, false);
            }
        }        
        AddVectorObs(distances);
        AddVectorObs(fraction);
    }


    void SetCenterOfMass()
    {
        _centerOfMass = Vector3.zero;
        float c = 0f;
        var bodyParts = this.gameObject.GetComponentsInChildren<Rigidbody>();
 
        foreach (var part in bodyParts)
        {
            _centerOfMass += part.worldCenterOfMass * part.mass;
            c += part.mass;
        }
        _centerOfMass /= c;
    }

    float StepRewardHopper101()
    {
        // float heightPenality = GetHeightPenality(1.1f);
        float uprightBonus = GetForwardBonus("pelvis");
        float velocity = GetVelocity("pelvis");
        float effort = GetEffort();
        // var effortPenality = 1e-3f * (float)effort;
        var effortPenality = 1e-1f * (float)effort;
        var jointsAtLimitPenality = GetJointsAtLimitPenality() * 4;

        if (_pain > 0f){
            uprightBonus = 0f;
        }
        if (_modeRecover) {
            uprightBonus = 0f;
            effortPenality = 0f;
        }

        var reward = velocity
            +uprightBonus
            // -heightPenality
            -effortPenality
            -jointsAtLimitPenality
            -_pain
            ;
        if (ShowMonitor) {
            // var hist = new []{reward,velocity,uprightBonus,-heightPenality,-effortPenality}.ToList();
            var hist = new []{reward, velocity, uprightBonus, -effortPenality, 
            -jointsAtLimitPenality,
            -_pain
            }.ToList();
            Monitor.Log("rewardHist", hist.ToArray());
        }
        _pain = 0f;
        return reward;
    }
}
