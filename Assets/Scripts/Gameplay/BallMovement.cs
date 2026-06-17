using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BallMovement : MonoBehaviour
{
    public bool displayDebugText;

    [SerializeField]
    Rigidbody rb;
    [SerializeField]
    Animator spriteAnim;

    UIScript UIref;
    GrappleSystem grappleSystem;

    [SerializeField]
    ParticleSystem ChargeFX;
    [SerializeField]
    ParticleSystem SpeedFX;
    [SerializeField]
    ParticleSystem DodgeFX;

    Controls _controls;

    Vector2 movementInput;

    float dodgeInput;

    [SerializeField]
    Camera tpCamera;
    Transform cameraTransform;

    public Material sphereMat1, sphereMat2, sphereMat3;

    public GameObject sphere;


    [SerializeField]
    Text debugText;

    public float maxVelocity;
    [SerializeField] float airMaxVelocity = 50f;
    public float inputForce;
    public float dodgeForce;
    public float dodgeTimer;
    public float dodgeCooldown;
    public float boostForce;
    public float boostMax;
    public float boostLv;
    float boostFXdur;
    public bool charging;
    public float playerMagnitudeBeforePhysicsUpdate;
    [SerializeField] private float movingSpeedThreshold = 1f;
    [SerializeField] float gravityScale = 0.4f;
    [SerializeField] float airStrafeForce = 1f;
    private MeshRenderer sphereRenderer;
    private HealthScript myHealth;
    

    void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;

        ChargeFX.Stop();

        sphereRenderer = sphere.GetComponent<MeshRenderer>();
        myHealth = GetComponent<HealthScript>();
        grappleSystem = GetComponent<GrappleSystem>();
        changeSphereColour(0);

        _controls = new Controls();
        rb = GetComponent<Rigidbody>();
        spriteAnim = GetComponentInChildren<Animator>();
        tpCamera = GameObject.Find("MainCamera").GetComponent<Camera>();
        UIref = GameObject.Find("Canvas").GetComponent<UIScript>();
        cameraTransform = tpCamera.GetComponent<Transform>();

        dodgeTimer = dodgeCooldown;
        boostLv = 0f;
        charging = false;

        _controls.Player.Movement.started += ctx => movementInput = ctx.ReadValue<Vector2>();
        _controls.Player.Movement.performed += ctx => movementInput = ctx.ReadValue<Vector2>();
        _controls.Player.Movement.canceled += ctx => movementInput = ctx.ReadValue<Vector2>();

        _controls.Player.Boost.performed += _ => boostPress();
        _controls.Player.Boost.canceled += _ => boostRelease();

        _controls.Player.Dodge.started += ctx => dodgeInput = ctx.ReadValue<float>();
        _controls.Player.Dodge.performed += __ => dodge();

        _controls.Player.Pause.started += ___ => UIref.togglePause();
    }

    void FixedUpdate()
    {   
        rb.AddForce(Vector3.up * Physics.gravity.magnitude * (1f - gravityScale), ForceMode.Acceleration);

        playerMagnitudeBeforePhysicsUpdate = rb.linearVelocity.magnitude;

        Vector3 directionInput = new Vector3(movementInput.x, 0f, movementInput.y);
        
        Vector3 relativeDirection = directionInput.x * cameraTransform.right + directionInput.z * new Vector3(cameraTransform.forward.x, 0f, cameraTransform.forward.z);

        if (relativeDirection != Vector3.zero)
            relativeDirection.Normalize();

        changeSphereColour(rb.linearVelocity.magnitude);

        bool hooked = grappleSystem != null && grappleSystem.anyHooked;

        if ((rb.linearVelocity.magnitude < maxVelocity && !charging) || hooked)
        {
            rb.AddForce(relativeDirection * inputForce * (hooked ? airStrafeForce : 1f));
        }

        if(charging && boostLv < boostMax)
        {
            boostLv += Time.deltaTime;
            rb.AddForce(Vector3.up * Physics.gravity.magnitude * 0.85f, ForceMode.Acceleration);
        }

        if(dodgeTimer < dodgeCooldown)
        {
            dodgeTimer += Time.deltaTime;
        }else if(dodgeTimer >= dodgeCooldown)
        {
            UIref.hideDodgeUI();
        }

        //spriteAnim.SetFloat("speed", rb.velocity.magnitude);

        bool grounded = Physics.Raycast(transform.position + Vector3.down * 0.55f, Vector3.down, 0.65f);
        float cap = grounded ? maxVelocity : airMaxVelocity;
        if (rb.linearVelocity.magnitude > cap)
            rb.linearVelocity = rb.linearVelocity.normalized * cap;

        if(displayDebugText)
        {
            debugText.text = 
            " player input= " + movementInput.ToString() +
            "\n mouse input= " + _controls.Player.Camera.ReadValue<Vector2>().ToString() +
            "\n \n directionInput= " + directionInput.ToString() + 
            "\n \n camera right transform = " + cameraTransform.right.ToString() +
            "\n camera forward transform= " + cameraTransform.forward.ToString() +
            "\n \n relatve direction= " + relativeDirection.ToString() +
            "\n \n player speed= " + rb.linearVelocity.magnitude.ToString() +
            "\n \n player boost= " + boostLv.ToString() +
            "\n boost FX duration= " + boostFXdur.ToString() + 
            "\n \n player dodge= " + dodgeInput.ToString() +
            "\n dodge timer= " + dodgeTimer.ToString() +
            ""
            ;
        }
    }

    void boostPress()
    {
        rb.linearVelocity *= 0.3f;
        UIref.showBoostUI();
        boostLv = 0.1f;
        charging = true;
        ChargeFX.Play();
    }

    void boostRelease()
    {
        ChargeFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        SpeedFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = SpeedFX.main;
        main.duration = (boostLv*3f)/10f;

        rb.AddForce(cameraTransform.forward * (boostLv * boostForce), ForceMode.Impulse);
        boostLv = 0f;
        charging = false;
        changeSphereColour(rb.linearVelocity.magnitude);

        boostFXdur = main.duration;
        SpeedFX.Play();
        UIref.hideBoostUI();
    }

    void dodge()
    {
        if(dodgeTimer >= dodgeCooldown)
        {
            if(dodgeInput < 0f)
            {
                DodgeFX.Play();
                UIref.showDodgeUI();
                rb.AddForce(new Vector3(-cameraTransform.right.x, 0f, -cameraTransform.right.z) * (dodgeForce), ForceMode.Impulse);
                dodgeTimer = 0f;
            }else if(dodgeInput > 0f)
            {
                DodgeFX.Play();
                UIref.showDodgeUI();
                rb.AddForce(new Vector3(cameraTransform.right.x, 0f, cameraTransform.right.z) * (dodgeForce), ForceMode.Impulse);
                dodgeTimer = 0f;
            }
        }
    }

    private void changeSphereColour(float x)
    {
        if(charging)
        { 
            sphereRenderer.material = sphereMat3; 
        }
        else if (x > movingSpeedThreshold)
        {
            sphereRenderer.material = sphereMat2;
        }
        else
        {
            sphereRenderer.material = sphereMat1;
        }
    }

    private void OnEnable()
    {
        _controls.Player.Enable();
    }
    private void OnDisable()
    {
        _controls.Player.Disable();
    }

    void OnCollisionEnter(Collision other)
    {
        if (other.transform.CompareTag("Enemy"))
        {   
            if (!other.gameObject.TryGetComponent<EnemyAI>(out var enemyAI))
                return;

            float playerCollisionSpeed = playerMagnitudeBeforePhysicsUpdate;
            float otherCollisionSpeed = enemyAI.enemyMagnitudeBeforePhysicsUpdate;

            /*
            Debug.Log("player collision speed: " + playerCollisionSpeed); 
            Debug.Log("enemy collision speed: " + otherCollisionSpeed);
            */
            
            //whoever was going slower before the collision takes damage
            if (otherCollisionSpeed > playerCollisionSpeed && otherCollisionSpeed > 30f)
            {
                myHealth.takeDmg();
                Debug.Log("enemy was the faster object, player takes dmg");

            }else if(playerCollisionSpeed > otherCollisionSpeed && playerCollisionSpeed > 30f)
            {
                other.transform.gameObject.GetComponent<HealthScript>().takeDmg();
                Debug.Log("player was the faster object, enemy takes dmg");
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Strawberry"))
        {   
            Destroy(other.gameObject);
            UIref.strawbCollected += 1;
            myHealth.healDmg();
        }
    }
}
