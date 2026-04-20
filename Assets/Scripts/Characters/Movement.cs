using UnityEngine;

public abstract class Movement : MonoBehaviour
{
    public float speed = 1.5f; // be slower than player
    protected Rigidbody rb;
    public Vector3 movement;
    public bool canMove = true;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    protected abstract void FixedUpdate();


    protected void moveCharacter(Vector3 direction)
    {
        rb.velocity = direction * speed;
    }
}

