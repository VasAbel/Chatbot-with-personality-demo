using UnityEngine;
using UnityEngine.AI;

public class NpcMovement : Movement
{
    private Transform destination;
    private NavMeshAgent agent;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            
            agent.speed = speed; 
            agent.angularSpeed = 720f;
            agent.acceleration = 2f;
            agent.stoppingDistance = 0.02f;
            agent.updateRotation = true;
            agent.updateUpAxis = true;
        }
    }

    void Update()
    {
        if (!canMove)
        {
            movement = Vector3.zero;
            if (agent) agent.isStopped = true;
            return;
        }

        if (agent != null)
        {
            // NavMeshAgent mode
            if (destination != null)
            {
                if (agent.isStopped) agent.isStopped = false;
                if (!agent.hasPath || agent.destination != destination.position)
                    agent.SetDestination(destination.position);

                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    agent.isStopped = true;
                    destination = null;
                    canMove = false;
                }
            }
            else
            {
                agent.isStopped = true;
            }
        }
        else
        {
            // Fallback: straight-line mode (original logic)
            if (destination != null)
            {
                Vector3 direction = (destination.position - transform.position).normalized;
                movement = direction;
                float distance = Vector3.Distance(transform.position, destination.position);
                if (distance < 0.2f) // slightly larger than before to avoid jitter
                {
                    movement = Vector3.zero;
                    canMove = false;
                    destination = null;
                }
            }
            else
            {
                movement = Vector3.zero;
            }
        }
    }

    protected override void FixedUpdate()
    {
        // Only drive Rigidbody when not using NavMeshAgent
        if (agent == null)
            moveCharacter(movement);
    }

    public void MoveTo(string placeToGo)
    {
        Transform target = PlaceRegistry.Instance.GetPlaceByName(placeToGo);
        if (target != null)
        {
            destination = target;
            canMove = true;

            if (agent != null)
            {
                agent.isStopped = false;
                agent.SetDestination(target.position);
            }
        }
    }
}
