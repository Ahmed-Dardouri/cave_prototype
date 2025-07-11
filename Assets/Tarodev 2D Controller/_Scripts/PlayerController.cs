using System;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine;

public class PlayerController : MonoBehaviour, IPlayerController
{
        
    public ScriptableStats _stats;
    private Rigidbody2D _rb;
    private CapsuleCollider2D _col;
    private FrameInput _frameInput;
    private Vector2 _frameVelocity;
    private bool _cachedQueryStartInColliders;

    public LayerMask wallLayer;
    public LayerMask groundLayer;

    private MovementTracker _MovementTracker;
    private BlastController _BlastController;
    private DashController _DashController;
    private AttackController _AttackController;
    private StickToWallController _StickToWallController;
    private JumpController _JumpController;

    #region input control

    public InputActionReference jumpAction;
    public InputActionReference dashAction;
    public InputActionReference moveAction;
    public InputActionReference attackAction;
    public InputActionReference blastAction;

    #endregion


    private bool _applyGravityCheck;

    #region Interface

    public Vector2 FrameInput => _frameInput.Move;
    public event Action<bool, float> GroundedChanged;
    public event Action Jumped;

    public GameObject BlastPrefab;
    public GameObject AttackPrefab; 

    #endregion

    private float _time;

    #region Start

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<CapsuleCollider2D>();

        _cachedQueryStartInColliders = Physics2D.queriesStartInColliders;

        _applyGravityCheck = false;
        
        AttackControllerInit();
        BlastControllerInit();
        MovementTrackerInit();
        DashControllerInit();
        StickToWallControllerInit();
        JumpControllerInit();
    }

    private void StickToWallControllerInit(){
        _StickToWallController.StickLeft = false;
        _StickToWallController.StickRight = false;
        _StickToWallController.StickToWallEnabled = true;
        _StickToWallController.Stuck = false;
    }
    
    private void AttackControllerInit(){
        _AttackController.prefab = AttackPrefab;
        _AttackController.PrefabSpawnDynamicOffset = 2f;
        _AttackController.PrefabSpawnStaticOffset = new Vector2(0, 0.7f);
        _AttackController.PrefabLifetime = 0.1f;
        _AttackController.direction = new Vector2();

        _AttackController.dashController.velocity = 26f;
        _AttackController.dashController.duration = 0.05f;
        _AttackController.dashController.cooldown = 0.4f;
        _AttackController.dashController.cooldownPassed = false;
        _AttackController.dashController.isDashing = false;
        _AttackController.dashController.canDash = true;
    }

    private void BlastControllerInit(){
        _BlastController.prefab = BlastPrefab;
        _BlastController.PrefabSpawnDynamicOffset = 2f;
        _BlastController.PrefabSpawnStaticOffset = new Vector2(0, 0.7f);
        _BlastController.PrefabLifetime = 0.1f;
        _BlastController.direction = new Vector2();
        _BlastController.powerMultiplier = 1;

        _BlastController.dashController.velocity = 26f;
        _BlastController.dashController.duration = 0.05f;
        _BlastController.dashController.cooldown = 0.4f;
        _BlastController.dashController.cooldownPassed = true;
        _BlastController.dashController.isDashing = false;
        _BlastController.dashController.canDash = true;
    }
    
    private void MovementTrackerInit(){
        _MovementTracker.lastMove = new Vector2(1f, 0);
        _MovementTracker.lastHorizontalMove = 0f;
        _MovementTracker.lastVeticalMove = 0f;
        _MovementTracker.horizontalIsPressed = false;
        _MovementTracker.horizontalIsPressed_prevState = false;
        _MovementTracker.verticalIsPressed = false;
        _MovementTracker.verticalIsPressed_prevState = false;
    }

    private void DashControllerInit(){
        _DashController.velocity = 28f;
        _DashController.duration = 0.1f;
        _DashController.cooldown = 0.4f;
        _DashController.direction = new Vector2();
        _DashController.isDashing = false;
        _DashController.cooldownPassed = true;
        _DashController.canDash = true;
    }

    private void JumpControllerInit(){
        _JumpController.JumpToConsume = false;
        _JumpController.BufferedJumpUsable = false;
        _JumpController.EndedJumpEarly = false;
        _JumpController.CoyoteUsable = false;
        _JumpController.TimeJumpWasPressed = 0;
        _JumpController.canJump = true;
        _JumpController.jumpEnabled = true;
    }

    #endregion

    private void Update()
    {
        _time += Time.deltaTime;
        GatherInput();

        CheckAttackTask();
        CheckBlastTask();
        CheckDashTask();
        MovementTrackerTask();
        StickToWallCheck();
        ApplyGravityCheck();
        
    }





    #region Attack
    
    private void AttackPrefabCreate(Vector2 direction)
    {
        // Determine the rotation based on the direction vector
        Quaternion rotation = Quaternion.identity;
        if (direction == Vector2.right)
            rotation = Quaternion.Euler(0, 0, 0);
        else if (direction == Vector2.left)
            rotation = Quaternion.Euler(0, 0, 180);
        else if (direction == Vector2.up)
            rotation = Quaternion.Euler(0, 0, 90);
        else if (direction == Vector2.down)
            rotation = Quaternion.Euler(0, 0, -90);
        else{
            Debug.Log("direction error, direction: " + direction);
        }

        

        // Calculate the spawn position using an offset so it's in front of the player
        Vector2 spawnPosition = (Vector2)transform.position + _AttackController.PrefabSpawnStaticOffset + direction * _AttackController.PrefabSpawnDynamicOffset;

        // Instantiate the blast prefab at the spawn position with the correct rotation
        GameObject blast = Instantiate(_AttackController.prefab, spawnPosition, rotation);

        // Destroy the blast after its lifetime expires
        Destroy(blast, _AttackController.PrefabLifetime);
    }

    private void CheckAttackTask(){
        if(attackAction.action.WasPressedThisFrame() && _AttackController.dashController.canDash){

            _AttackController.dashController.isDashing = false; /* pogo might be added */
            _AttackController.dashController.canDash = false;  
            _AttackController.dashController.cooldownPassed = false;

            _AttackController.direction = _MovementTracker.lastMove * -1;

            AttackPrefabCreate(_AttackController.direction * -1);

            if(_AttackController.direction == Vector2.zero){
                _AttackController.direction = new Vector2(transform.localScale.x, 0);
            }
            // stop blast
            StartCoroutine(StopAttack());
        }
        RunAttack();
    }

    private IEnumerator StopAttack(){
        yield return new WaitForSeconds(_AttackController.dashController.duration);
        _AttackController.dashController.isDashing = false;
        yield return new WaitForSeconds(_AttackController.dashController.cooldown);
        _AttackController.dashController.cooldownPassed = true;
    }

    private void RunAttack(){
        if(_AttackController.dashController.isDashing){
            _frameVelocity = _AttackController.direction.normalized * _AttackController.dashController.velocity;
        }else if(_AttackController.dashController.canDash == false && _AttackController.dashController.cooldownPassed){
            _AttackController.dashController.canDash = true;
        }
    }


    #endregion

    #region Blast

    private void BlastPrefabCreate(Vector2 direction)
    {
        // Determine the rotation based on the direction vector
        Quaternion rotation = Quaternion.identity;
        if (direction == Vector2.right)
            rotation = Quaternion.Euler(0, 0, -90);
        else if (direction == Vector2.left)
            rotation = Quaternion.Euler(0, 0, 90);
        else if (direction == Vector2.up)
            rotation = Quaternion.Euler(0, 0, 0);
        else if (direction == Vector2.down)
            rotation = Quaternion.Euler(0, 0, 180);
        else{
            Debug.Log("direction error, direction: " + direction);
        }

        
    
        // Calculate the spawn position using an offset so it's in front of the player
        Vector2 spawnPosition = (Vector2)transform.position + _BlastController.PrefabSpawnStaticOffset + direction * _BlastController.PrefabSpawnDynamicOffset;

        // Instantiate the blast prefab at the spawn position with the correct rotation
        GameObject blast = Instantiate(_BlastController.prefab, spawnPosition, rotation);

        // Destroy the blast after its lifetime expires
        Destroy(blast, _BlastController.PrefabLifetime);
    }

    private void CheckBlastTask(){
        if(blastAction.action.WasPressedThisFrame() && _BlastController.dashController.canDash && _BlastController.dashController.cooldownPassed){
            _BlastController.dashController.isDashing = true;
            _BlastController.dashController.canDash = false;  
            _BlastController.dashController.cooldownPassed = false;
            Debug.Log("_MovementTracker.lastMove : " + _MovementTracker.lastMove.normalized);

            _BlastController.direction = _MovementTracker.lastMove.normalized;

            if((_BlastController.direction == Vector2.right && _StickToWallController.StickRight) || (_BlastController.direction == Vector2.left && _StickToWallController.StickLeft)){
                _BlastController.powerMultiplier = 1.3f;
            }else if (_StickToWallController.Stuck && _BlastController.direction.y != 0){
                _BlastController.powerMultiplier = 0;
            }else{
                _BlastController.powerMultiplier = 1;
            }

            // _StickToWallController.StickToWallEnabled = false;

            BlastPrefabCreate(_BlastController.direction);

            if(_BlastController.direction == Vector2.zero){
                _BlastController.direction = new Vector2(transform.localScale.x, 0);
            }
            // stop blast
            StartCoroutine(StopBlast());
        }
        RunBlast();
    }

    private IEnumerator StopBlast(){
        yield return new WaitForSeconds(_BlastController.dashController.duration);
        _BlastController.dashController.isDashing = false;
        // _StickToWallController.StickToWallEnabled = true;
        yield return new WaitForSeconds(_BlastController.dashController.cooldown);
        _BlastController.dashController.cooldownPassed = true;
    }

    private void RunBlast(){
        if(_BlastController.dashController.isDashing){
            _frameVelocity = _BlastController.direction * _BlastController.dashController.velocity * _BlastController.powerMultiplier * -1;
        }else if(_grounded || _StickToWallController.Stuck){
            _BlastController.dashController.canDash = true;
        }
    }

    #endregion

    #region stick_to_wall

    private void StickToWallCheck(){
        _StickToWallController.StickLeft = false;
        _StickToWallController.StickRight = false;
        if(_StickToWallController.StickToWallEnabled){
            bool _leftWallHit;
            bool _rightWallHit;
            _leftWallHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.left, _stats.WallerDistance, wallLayer);
            _rightWallHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.right, _stats.WallerDistance, wallLayer);
            if(!_grounded && _frameVelocity.y <= 0){
                if(_leftWallHit){
                    _StickToWallController.StickLeft = true;
                }else if(_rightWallHit){
                    _StickToWallController.StickRight = true;
                }
            }
        }
        _StickToWallController.Stuck = _StickToWallController.StickLeft || _StickToWallController.StickRight;
    }

    #endregion

    private void MovementTrackerTask(){
        Vector2 Move = moveAction.action.ReadValue<Vector2>();
        _MovementTracker.horizontalIsPressed    = Move.x != 0;
        _MovementTracker.verticalIsPressed      = Move.y != 0;

        if(_MovementTracker.horizontalIsPressed){
            _MovementTracker.lastHorizontalMove = Move.x;
            _MovementTracker.lastMove = new Vector2(Move.x, 0);
        }

        if(_MovementTracker.verticalIsPressed){
            _MovementTracker.lastVeticalMove = Move.y;
            _MovementTracker.lastMove = new Vector2(0, Move.y);
        }

        _MovementTracker.horizontalIsPressed_prevState = _MovementTracker.horizontalIsPressed;
        _MovementTracker.verticalIsPressed_prevState = _MovementTracker.verticalIsPressed;
    }

    private void GatherInput()
    {
        _frameInput = new FrameInput
        {
            JumpDown = jumpAction.action.WasPressedThisFrame(),
            JumpHeld = jumpAction.action.IsPressed(),
            Move = moveAction.action.ReadValue<Vector2>()
        };


        if (_stats.SnapInput)
        {
            _frameInput.Move.x = Mathf.Abs(_frameInput.Move.x) < _stats.HorizontalDeadZoneThreshold ? 0 : Mathf.Sign(_frameInput.Move.x);
            _frameInput.Move.y = Mathf.Abs(_frameInput.Move.y) < _stats.VerticalDeadZoneThreshold ? 0 : Mathf.Sign(_frameInput.Move.y);
            
        }

        if (_frameInput.JumpDown)
        {
            _JumpController.JumpToConsume = true;
            _JumpController.TimeJumpWasPressed = _time;
        }
    }

    private void ApplyGravityCheck(){
        if(_DashController.isDashing == true || _BlastController.dashController.isDashing == true || _StickToWallController.Stuck){
            _applyGravityCheck = false;
        }else{
            _applyGravityCheck = true;
        }
    }

    #region Dash

    private void CheckDashTask(){
        if(dashAction.action.WasPressedThisFrame() && _DashController.canDash && _DashController.cooldownPassed){

            _DashController.isDashing = true;
            _DashController.canDash = false;  
            _DashController.cooldownPassed = false;

            _DashController.direction = new Vector2(_MovementTracker.lastHorizontalMove, 0);

            if(_DashController.direction == Vector2.zero){
                _DashController.direction = new Vector2(transform.localScale.x, 0);
            }
            // stop dash
            StartCoroutine(StopDash());
        }
        RunDash();
    }

    private IEnumerator StopDash(){
        yield return new WaitForSeconds(_DashController.duration);
        _DashController.isDashing = false;
        yield return new WaitForSeconds(_DashController.cooldown);
        _DashController.cooldownPassed = true;
    }

    private void RunDash(){
        if(_DashController.isDashing){
            _frameVelocity = _DashController.direction.normalized * _DashController.velocity;
        }else if(_grounded || _StickToWallController.Stuck){
            _DashController.canDash = true;
        }
    }

    #endregion


    private void FixedUpdate()
    {
        CheckCollisions();

        HandleJump();

        // do not change order
        HandleHorizontalDirection();
        HandleVerticalDirection();
        HandleGravity();
        
        ApplyMovement();
    }

    #region Collisions
    
    private float _frameLeftGrounded = float.MinValue;
    private bool _grounded;

    private void CheckCollisions(){
        Physics2D.queriesStartInColliders = false;

        // Ground and Ceiling
        bool groundHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.down, _stats.GrounderDistance, groundLayer);
        bool ceilingHit = Physics2D.CapsuleCast(_col.bounds.center, _col.size, _col.direction, 0, Vector2.up, _stats.GrounderDistance, groundLayer);
        // Hit a Ceiling
        if (ceilingHit) _frameVelocity.y = Mathf.Min(0, _frameVelocity.y);

        // Landed on the Ground

        if (!_grounded && groundHit)
        {
            _grounded = true;
            _JumpController.CoyoteUsable = true;
            _JumpController.BufferedJumpUsable = true;
            _JumpController.EndedJumpEarly = false;
            GroundedChanged?.Invoke(true, Mathf.Abs(_frameVelocity.y));

        }
        // Left the Ground
        else if (_grounded && !groundHit)
        {
            _grounded = false;
            _frameLeftGrounded = _time;
            GroundedChanged?.Invoke(false, 0);
        }

        Physics2D.queriesStartInColliders = _cachedQueryStartInColliders;
    }

    #endregion

    #region Jumping

    private bool HasBufferedJump => _JumpController.BufferedJumpUsable && _time < _JumpController.TimeJumpWasPressed + _stats.JumpBuffer;
    private bool CanUseCoyote => _JumpController.CoyoteUsable && !_grounded && _time < _frameLeftGrounded + _stats.CoyoteTime;

    private void HandleJump()
    {
        _JumpController.canJump = false;
        if (!_JumpController.EndedJumpEarly && !_grounded && !_frameInput.JumpHeld && _rb.linearVelocity.y > 0){
            _JumpController.EndedJumpEarly = true;
        } 
        if (!_JumpController.JumpToConsume && !HasBufferedJump){
            return;
        }

        if(_StickToWallController.Stuck || _grounded || CanUseCoyote){
            _JumpController.canJump = true;
        }

        if(_JumpController.canJump){
            ExecuteJump();
        }
        
        _JumpController.JumpToConsume = false;


    }

    private void ExecuteJump()
    {
        _JumpController.EndedJumpEarly = false;
        _JumpController.TimeJumpWasPressed = 0;
        _JumpController.BufferedJumpUsable = false;
        _JumpController.CoyoteUsable = false;      
        _frameVelocity.y = _stats.JumpPower;
        if(_StickToWallController.StickLeft){
            _frameVelocity.x = _stats.JumpFromWallPower;
            _StickToWallController.Stuck = false;
        }else if(_StickToWallController.StickRight){
            _frameVelocity.x = -1 * _stats.JumpFromWallPower;
            _StickToWallController.Stuck = false;
        }
        Jumped?.Invoke();
    }

    #endregion

    #region Horizontal

    private void HandleHorizontalDirection()
    {
        if (_frameInput.Move.x == 0)
        {
            var deceleration = _grounded ? _stats.GroundDeceleration : _stats.AirDeceleration;
            _frameVelocity.x = Mathf.MoveTowards(_frameVelocity.x, 0, deceleration * Time.fixedDeltaTime);
        }
        else
        {
            _frameVelocity.x = Mathf.MoveTowards(_frameVelocity.x, _frameInput.Move.x * _stats.MaxSpeed, _stats.Acceleration * Time.fixedDeltaTime);
        }
    }

    #endregion

    #region vertical

    private void HandleVerticalDirection()
    {
        if(_StickToWallController.Stuck){
            _frameVelocity.y = -1f;
        }
    }

    #endregion

    #region Gravity

    private void HandleGravity()
    {
        if (_grounded && _frameVelocity.y <= 0f){
            _frameVelocity.y = _stats.GroundingForce;
        }else if(_applyGravityCheck == true){
            var inAirGravity = _stats.FallAcceleration;
            if (_JumpController.EndedJumpEarly && _frameVelocity.y > 0) inAirGravity *= _stats.JumpEndEarlyGravityModifier;
            _frameVelocity.y = Mathf.MoveTowards(_frameVelocity.y, -_stats.MaxFallSpeed, inAirGravity * Time.fixedDeltaTime);
        }
    }

    #endregion

    private void ApplyMovement() => _rb.linearVelocity = _frameVelocity;

}

public struct FrameInput
{
    public bool JumpDown;
    public bool JumpHeld;
    public Vector2 Move;
}

public struct DashController
{
    public float velocity;
    public float duration;
    public float cooldown;
    public bool cooldownPassed;
    public Vector2 direction;
    public bool isDashing;
    public bool canDash;
}

public struct BlastController
{
    public DashController dashController;
    public float PrefabLifetime;
    public GameObject prefab;
    public float PrefabSpawnDynamicOffset;
    public Vector2 PrefabSpawnStaticOffset;
    public Vector2 direction;
    public float powerMultiplier;
}

public struct AttackController
{
    public DashController dashController;
    public float PrefabLifetime;
    public GameObject prefab;
    public float PrefabSpawnDynamicOffset;
    public Vector2 PrefabSpawnStaticOffset;
    public Vector2 direction;
}

public struct StickToWallController
{
    public bool StickLeft;
    public bool StickRight;
    public bool StickToWallEnabled;
    public bool Stuck;
}

public struct JumpController
{
    public bool JumpToConsume;
    public bool BufferedJumpUsable;
    public bool EndedJumpEarly;
    public bool CoyoteUsable;
    public float TimeJumpWasPressed;
    public bool canJump;
    public bool jumpEnabled;
}


public struct MovementTracker
{
    public Vector2 lastMove;
    public float lastHorizontalMove;
    public float lastVeticalMove;
    public bool horizontalIsPressed_prevState;
    public bool verticalIsPressed_prevState;
    public bool horizontalIsPressed;
    public bool verticalIsPressed;
}

public interface IPlayerController
{
    public event Action<bool, float> GroundedChanged;

    public event Action Jumped;
    public Vector2 FrameInput { get; }
}

