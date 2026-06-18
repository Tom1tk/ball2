using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    public bool displayNavPath;

    [Header("Player reference")]
    [SerializeField]
    Transform player;
    public LayerMask playerLayer;

    [SerializeField]
    Rigidbody rb;
    [SerializeField]
    Animator spriteAnim;
    [SerializeField]
    ParticleSystem ChargeFX;
    [SerializeField]
    NavMeshAgent enemyAgent;
    [SerializeField]
    Collider wanderArea;

    public float maxVelocity = 15f;
    public float inputForce = 100f;
    public float detectionRange;
    public float boostAttackRange;
    public float boostChargeTime;
    public float boostCD = 3f;
    public float boostForce = 100f;
    public bool canSeePlayer, canAttackPlayer;
    public bool Attacked, boostStarted, wanderPointCreated;
    public float wanderTime;
    public Vector3 wanderPoint;
    public float enemyMagnitudeBeforePhysicsUpdate;

    private NavMeshPath cachedPath;
    private float pathUpdateCooldown = 0.2f;
    private float pathUpdateTimer;


    void Awake()
    {
        ChargeFX.Stop();
        rb = GetComponent<Rigidbody>();
        spriteAnim = GetComponentInChildren<Animator>();
        player = GameObject.Find("PlayerBall").GetComponent<Transform>();

        cachedPath = new NavMeshPath();

        Attacked = false;
        boostStarted = false;
        wanderPointCreated = false;

        //enemyAgent.updatePosition = false;
        //enemyAgent.updateRotation = false;
        //enemyAgent.updateUpAxis = false;
    }
    void FixedUpdate() 
    {
        enemyMagnitudeBeforePhysicsUpdate = rb.linearVelocity.magnitude;

        pathUpdateTimer += Time.fixedDeltaTime;

        //enemyAgent.SetDestination(player.position);

        //since the NavMeshAgent doesnt update its own position, we have to do it manually as the first corner [0]
        //enemyAgent.nextPosition = rb.position;

        //enemyAgent.Warp(rb.position);


        // Check if the ball is grounded.
        // The sphere radius is 0.5f, so starting a raycast at 0.55f below the center 
        // and shooting it 0.15f down avoids hitting the ball's own collider.
        RaycastHit hit;
        Vector3 rayStart = transform.position + Vector3.down * 0.55f;
        bool isGrounded = Physics.Raycast(rayStart, Vector3.down, out hit, 0.65f);
        

        //enemies don't have a run animation
        //spriteAnim.SetFloat("speed", rb.velocity.magnitude);

        canSeePlayer = Physics.CheckSphere(transform.position, detectionRange, playerLayer);
        canAttackPlayer = Physics.CheckSphere(transform.position, boostAttackRange, playerLayer);



        if (!canSeePlayer && !canAttackPlayer && !boostStarted && isGrounded)
        {
            if (wanderPointCreated == false)
            {
                StartCoroutine(createWanderPoint());
            }

            Wander();

        }

        if (canSeePlayer && !canAttackPlayer && !boostStarted && isGrounded)
        { 
            if (pathUpdateTimer >= pathUpdateCooldown)
            {   
                ChasePlayer();
                pathUpdateTimer = 0f;
            }
        }
            

        if (canSeePlayer && canAttackPlayer && !boostStarted)
        {
             boostStarted = true;
             StartCoroutine(BoostAtPlayer());

            //leftovers from CoD style health healing system
            //player.GetComponent<HealthScript>().combatStarted();
            //this.gameObject.GetComponent<HealthScript>().combatStarted();
         }
            
        
        
        if(displayNavPath)
        {
            DrawPath();
        }
        
    }
    IEnumerator createWanderPoint()
    {
        //finds a random point in collider for the enemy to "wander" to
        wanderPointCreated = true;
        wanderPoint = RandomPointInBounds(wanderArea.bounds);
        wanderTime = Random.Range(1f, 5f);
        yield return new WaitForSeconds(wanderTime);
        wanderPointCreated = false;
    }

    void Wander()
    {
        //NavMeshPath wanderPath = new NavMeshPath();

        //enemyAgent.CalculatePath(wanderPoint, cachedPath);
        //enemyAgent.SetPath(cachedPath);

        // Safety check on cachedPath corners
        //if (cachedPath.corners.Length >= 2)
        //{
        //    // Finds direction from agent to destination as Vector3
        //    Vector3 wanderDirection = (cachedPath.corners[1] - this.transform.position).normalized;
        //    //adds force in that direction
        //    rb.AddForce(wanderDirection * inputForce / 2f);
        //}
        //else
        {
            // Fallback: move directly towards the wanderPoint if path is too short or invalid
            Vector3 wanderDirection = (wanderPoint - this.transform.position).normalized;
            rb.AddForce(wanderDirection * inputForce / 2f);
        }
        if (player != null)
        {
            player.GetComponent<HealthScript>().combatEnded();
        }

        this.gameObject.GetComponent<HealthScript>().combatEnded();
    }

    void ChasePlayer()
    {
        //NavMeshPath pathToPlayer = new NavMeshPath();

        //enemyAgent.CalculatePath(player.position, cachedPath);
        //enemyAgent.SetPath(cachedPath);


        //if (cachedPath.corners.Length >= 2)
        //{
        //    // Since the first corner is the enemy location, use the second corner [1]
        //    Vector3 ChaseDirection = (cachedPath.corners[1] - this.transform.position).normalized;
        //    rb.AddForce(ChaseDirection * inputForce);
        //}
        //else if (player != null)
        {
            // Fallback: move directly towards the player
            Vector3 ChaseDirection = (player.position - this.transform.position).normalized;
            rb.AddForce(ChaseDirection * inputForce);
        }
        if (player != null)
        {
            player.GetComponent<HealthScript>().combatStarted();
        }

        this.gameObject.GetComponent<HealthScript>().combatStarted();
    }

    IEnumerator BoostAtPlayer()
    {
        if (!Attacked)
        {
            Attacked = true;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);

            ChargeFX.Play();

            yield return new WaitForSeconds(boostChargeTime);

            ChargeFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            this.transform.LookAt(player);
            rb.AddForce(transform.forward * (boostForce), ForceMode.Impulse);
            
            Invoke(nameof(ResetAttack), boostCD);

        }
    }

    private void ResetAttack()
    {
        Attacked = false;
        boostStarted = false;
    }

    public static Vector3 RandomPointInBounds(Bounds bounds) 
    {
        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            Random.Range(bounds.min.z, bounds.max.z)
            );
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, boostAttackRange);
    }

    void DrawPath()
    {
        //var nav = GetComponent<NavMeshAgent>();
        //if(nav == null || nav.path == null)
        //    return;

        //var line = this.GetComponent<LineRenderer>();
        //if(line == null)
        //{
        //    line = this.gameObject.AddComponent<LineRenderer>();
        //    line.material = new Material(Shader.Find("Sprites/Default")) {color = Color.yellow};
        //    line.startWidth =  0.25f;
        //    line.startColor = Color.red;
        //}

        //var path = nav.path;

        //line.positionCount = path.corners.Length;

        //for( int i = 0; i < path.corners.Length; i++ )
        //{
        //    line.SetPosition( i, path.corners[ i ] );
        //}

        /*
        Draws a yellow line from the center of the actor to the clicked location with the code in the Update()
        */
    }

}
