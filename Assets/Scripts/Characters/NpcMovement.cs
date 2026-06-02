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

    // --- Personal space / soft separation ---
    // NPCs pass through each other (no physics collision), so when several settle on the
    // same spot they visually stack and it's unclear who the player is addressing.
    // This applies a gentle, capped nudge that spreads stacked NPCs slightly apart.
    [Header("Personal Space")]
    [Tooltip("NPCs closer than this (meters) gently push apart so they don't stack on one spot.")]
    public float separationRadius = 1.1f;
    [Tooltip("Max nudge speed (units/sec). Keep small so it never shoves or jitters.")]
    public float separationSpeed = 0.6f;

    // Cheap registry of all NPC movers so each can find its neighbours without scene scans.
    private static readonly System.Collections.Generic.List<NpcMovement> _allMovers =
        new System.Collections.Generic.List<NpcMovement>();

    void OnEnable()
    {
        if (!_allMovers.Contains(this)) _allMovers.Add(this);
    }

    void OnDisable()
    {
        _allMovers.Remove(this);
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed = speed;
            agent.angularSpeed = 720f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.3f;
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            // NPCs pass through each other — no physical pushing between agents.
            // Conversation detection uses trigger colliders, not physics contact.
            agent.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.NoObstacleAvoidance;
        }
        _spawnPos = transform.position;
    }

    void Update()
    {
        // Soft separation: only nudges idle, non-conversing NPCs slightly apart so they
        // don't perfectly stack on the same spot. Skipped during conversations so NPCs
        // can't be pushed out of each other's trigger zones mid-chat.
        ApplySeparation();

        if (!canMove)
        {
            movement = Vector3.zero;
            if (agent != null)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
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
        // All attempts failed � just wait and retry next frame
        _wanderIdleTimer = 1f;
    }

    protected override void FixedUpdate()
    {
        // Only drive Rigidbody when not using NavMeshAgent
        if (agent == null)
            moveCharacter(movement);
    }

    // Gently steers this NPC away from any other NPCs standing too close, so a group that
    // settles on one spot eases into a small cluster instead of perfectly overlapping.
    // Uses agent.Move so the NPC stays on the NavMesh (can't fall off or get wedged), only
    // reacts to other NpcMovement agents (never the player), and fades to zero at the
    // comfortable radius so there's no jitter or shoving.
    private void ApplySeparation()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (separationRadius <= 0f) return;

        // NEVER nudge an NPC that is currently in a conversation.
        // agent.Move() would push them out of the other NPC's trigger collider,
        // firing OnTriggerExit and ending the conversation early while the LLM
        // keeps running — the "walked-away mid-chat" bug.
        NPC npcComp = GetComponent<NPC>();
        if (npcComp != null && npcComp.isInConversation) return;

        // Also skip while actively travelling — no need to nudge a moving agent.
        bool activelyMoving = canMove && agent.hasPath && !agent.isStopped;
        if (activelyMoving) return;

        Vector3 myPos = transform.position;
        Vector3 push = Vector3.zero;
        int neighbours = 0;

        for (int i = 0; i < _allMovers.Count; i++)
        {
            var other = _allMovers[i];
            if (other == null || other == this) continue;

            Vector3 diff = myPos - other.transform.position;
            diff.y = 0f;
            float dist = diff.magnitude;

            // Nearly identical positions: pick a stable per-NPC direction so they don't
            // freeze on top of each other or fight over the same axis.
            if (dist < 0.0001f)
            {
                float a = GetInstanceID() * 0.123f;
                diff = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                dist = 0.01f;
            }

            if (dist < separationRadius)
            {
                float strength = 1f - (dist / separationRadius); // 1 at contact → 0 at radius
                push += (diff / dist) * strength;
                neighbours++;
            }
        }

        if (neighbours == 0) return;

        Vector3 velocity = Vector3.ClampMagnitude(push, 1f) * separationSpeed;
        agent.Move(velocity * Time.deltaTime);
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
                // Small random offset so multiple NPCs heading to the same location
                // don't all stack on the exact same point and push each other
                Vector3 offset = new Vector3(
                    Random.Range(-0.7f, 0.7f),
                    0f,
                    Random.Range(-0.7f, 0.7f)
                );
                agent.isStopped = false;
                agent.SetDestination(target.position + offset);
            }
        }
    }
}