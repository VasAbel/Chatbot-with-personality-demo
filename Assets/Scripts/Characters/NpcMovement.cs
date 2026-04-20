using UnityEngine;
using UnityEngine.AI;

public class NpcMovement : Movement
{
    private Transform destination;
    private NavMeshAgent agent;

    private string currentTargetPlaceId;
    public string CurrentTargetPlaceId => currentTargetPlaceId;

    // --- Wander / test mode ---
    // Set WanderMode = true to ignore the schedule and roam randomly.
    // Expose in Inspector so you can toggle it at runtime without code.
    [Header("Test Mode")]
    public bool WanderMode = false;
    [Tooltip("How many seconds to wait at each random spot before picking the next one.")]
    public float wanderIdleSeconds = 1.5f;
    [Tooltip("Max distance from spawn position to wander. Set to match your map size.")]
    public float wanderRadius = 8f;

    private Vector3 _spawnPos;
    private float _wanderIdleTimer = 0f;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed = speed;
            agent.angularSpeed = 720f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.02f;
            agent.updateRotation = false;
            agent.updateUpAxis = false;
        }
        _spawnPos = transform.position;
    }

    void Update()
    {
        if (!canMove)
        {
            movement = Vector3.zero;
            if (agent) agent.isStopped = true;
            return;
        }

        // --- Wander mode: pick random NavMesh points, ignore schedule ---
        if (WanderMode)
        {
            UpdateWander();
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

            if (agent.velocity.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = look;
                }
            }
            else
            {
                var e = transform.rotation.eulerAngles;
                transform.rotation = Quaternion.Euler(0f, e.y, 0f);
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
                if (distance < 0.2f)
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

    private void UpdateWander()
    {
        if (agent == null) return;

        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

        if (arrived)
        {
            _wanderIdleTimer -= Time.deltaTime;
            if (_wanderIdleTimer <= 0f)
                PickRandomWanderPoint();
        }

        // Mirror rotation logic for wander movement too
        if (agent.velocity.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    private void PickRandomWanderPoint()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 rand2d = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = _spawnPos + new Vector3(rand2d.x, 0f, rand2d.y);

            if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
                _wanderIdleTimer = wanderIdleSeconds;
                return;
            }
        }
        // All attempts failed — just wait and retry next frame
        _wanderIdleTimer = 1f;
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
            currentTargetPlaceId = placeToGo;
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