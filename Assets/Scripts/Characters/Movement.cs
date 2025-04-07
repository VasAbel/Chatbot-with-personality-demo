using UnityEngine;

public abstract class Movement : MonoBehaviour
{
    public float speed = 0.1f;
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

