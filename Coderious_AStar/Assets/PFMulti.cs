using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

public class PFMulti : MonoBehaviour
{
    public struct Node
    {
        public int2 coord;
        public int2 parent;
        public float gScore;
        public float hScore;
        public float fScore;
    }

    Hashtable obstacles, starts;
    Node end;
    int safeGuard = 1000;

    public Tilemap map;
    public Tile defaultTile;
    public Camera cam;

    public GameObject tilePrefab;

    [SerializeField]
    float unitSpeed = 5f;


    // Start is called before the first frame update
    void Start()
    {
        obstacles = new Hashtable();
        starts = new Hashtable();
        end = new Node { coord = int2.zero, parent = int2.zero, gScore = int.MaxValue, hScore = int.MaxValue };
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButtonDown(0))
        {
            PlaceStart();
        }

        if (Input.GetKey(KeyCode.LeftControl) && Input.GetMouseButtonDown(0))
        {
            PlaceEnd();
        }

        if (Input.GetMouseButtonDown(0) &&
            !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.LeftControl))
        {
            PlaceObstacle();
        }

        if(Input.GetKeyDown(KeyCode.Space))
        {
            ClearTiles();

            float startTime = Time.realtimeSinceStartup;

            FindPath();

            float endTime = Time.realtimeSinceStartup;
            Debug.Log(endTime - startTime);
        }
            
    }

    void ClearTiles()
    {
        map.ClearAllTiles();

        foreach (int2 s in starts.Keys)
        {
            Vector3Int start = new Vector3Int(s.x, s.y, 0);
            map.SetTile(start, defaultTile);
            map.SetTileFlags(start, TileFlags.None);
            map.SetColor(start, Color.green);
        }

        Vector3Int _end = new Vector3Int(end.coord.x, end.coord.y, 0);
        map.SetTile(_end, defaultTile);
        map.SetTileFlags(_end, TileFlags.None);
        map.SetColor(_end, Color.red);

        foreach(int2 o in obstacles.Keys)
        {
            Vector3Int obstacle = new Vector3Int(o.x, o.y, 0);
            map.SetTile(obstacle, defaultTile);
            map.SetTileFlags(obstacle, TileFlags.None);
            map.SetColor(obstacle, Color.black);
        }
    }

    void PlaceStart()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if (starts.ContainsKey(coord))
        {
            map.SetTile(new Vector3Int(coord.x, coord.y, 0), null);
            starts.Remove(coord);
        }
        else if (!obstacles.ContainsKey(coord) && !coord.Equals(end.coord))
        {
            Node startNode = new Node
            {
                coord = coord,
                parent = int2.zero,
                gScore = 0,
                hScore = float.MaxValue
            };

            starts.Add(coord, startNode);
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.green);
        }
    }

    void PlaceEnd()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if(!obstacles.ContainsKey(coord) && !starts.ContainsKey(coord))
        {
            map.SetTile(new Vector3Int(end.coord.x, end.coord.y, 0), null);

            end.coord = coord;
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.red);
        }

    }

    void PlaceObstacle()
    {
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int mouseCell = map.WorldToCell(mouseWorldPos);
        int2 coord = new int2 { x = mouseCell.x, y = mouseCell.y };

        if(obstacles.ContainsKey(coord))
        {
            map.SetTile(new Vector3Int(coord.x, coord.y, 0), null);
            obstacles.Remove(coord);
        }
        else if(!starts.ContainsKey(coord) && !coord.Equals(end.coord))
        {
            obstacles.Add(coord, true);
            map.SetTile(mouseCell, defaultTile);
            map.SetTileFlags(mouseCell, TileFlags.None);
            map.SetColor(mouseCell, Color.black);
        }
    }

    public void FindPath()
    {
        NativeHashMap<int2, bool> isObstacle = 
            new NativeHashMap<int2, bool>(obstacles.Count, Allocator.TempJob);
        NativeArray<int2> offsets = new NativeArray<int2>(8, Allocator.TempJob);
        NativeArray<Node> startNative = new NativeArray<Node>(starts.Count, Allocator.TempJob);
        NativeMultiHashMap<int2, Node> results =
            new NativeMultiHashMap<int2, Node>(starts.Count * safeGuard, Allocator.TempJob);

        foreach(int2 o in obstacles.Keys)
        {
            isObstacle.Add(o, true);
        }

        int counter = 0;

        foreach(Node n in starts.Values)
        {
            startNative[counter] = n;
            counter++;
        }

        offsets[0] = new int2(0, 1);
        offsets[1] = new int2(1, 1);
        offsets[2] = new int2(1, 0);
        offsets[3] = new int2(1, -1);
        offsets[4] = new int2(0, -1);
        offsets[5] = new int2(-1, -1);
        offsets[6] = new int2(-1, 0);
        offsets[7] = new int2(-1, 1);

        AStar aStar = new AStar
        {
            isObstacle = isObstacle,
            offsets = offsets,
            startNative = startNative,
            results = results,
            end = end,
            safeGuard = safeGuard
        };

        JobHandle handle = aStar.Schedule(starts.Count, 16);
        handle.Complete();

        NativeKeyValueArrays<int2, Node> keyValueArray = results.GetKeyValueArrays(Allocator.Temp);
        Dictionary<int2, Queue<Node>> waypoints = new Dictionary<int2, Queue<Node>>();

        for(int i = 0; i < keyValueArray.Keys.Length; i++)
        {
            if(!waypoints.ContainsKey(keyValueArray.Keys[i]))
            {
                waypoints.Add(keyValueArray.Keys[i], new Queue<Node>());
                waypoints[keyValueArray.Keys[i]].Enqueue(keyValueArray.Values[i]);
            }
            else
            {
                waypoints[keyValueArray.Keys[i]].Enqueue(keyValueArray.Values[i]);
            }
        }

        foreach(int2 start in waypoints.Keys)
        {
            StartCoroutine(MoveUnitCoroutine(start, waypoints[start]));
        }

        startNative.Dispose();
        isObstacle.Dispose();
        offsets.Dispose();
        results.Dispose();            
    }

    IEnumerator MoveUnitCoroutine(int2 start, Queue<Node> waypoints)
    {
        Vector3 startPos = map.GetCellCenterWorld(new Vector3Int(start.x, start.y, 0));
        startPos.x -= map.cellGap.x / 2f;
        startPos.y -= map.cellGap.y / 2f;
        GameObject unit = Instantiate(tilePrefab, new Vector3(startPos.x, startPos.y, 0),
            Quaternion.identity);

        Node waypoint = waypoints.Dequeue();

        while(waypoints.Count != 0)
        {
            Vector3 nextWP = map.GetCellCenterWorld(new Vector3Int(waypoint.coord.x, waypoint.coord.y, 0));
            nextWP.x -= map.cellGap.x / 2f;
            nextWP.y -= map.cellGap.y / 2f;

            while((nextWP - unit.transform.position).magnitude > 0.1f)
            {
                unit.transform.Translate((nextWP - unit.transform.position).normalized * Time.deltaTime
                    * unitSpeed);

                yield return null;
            }

            waypoint = waypoints.Dequeue();
        }

        GameObject.Destroy(unit);
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct AStar : IJobParallelFor
    {
        [ReadOnly] public NativeHashMap<int2, bool> isObstacle;
        [ReadOnly] public NativeArray<int2> offsets;
        [ReadOnly] public NativeArray<Node> startNative;
        [NativeDisableParallelForRestriction] public NativeMultiHashMap<int2, Node> results;

        public Node start;
        public Node end;

        public int safeGuard;

        public void Execute(int r)
        {
            NativeHashMap<int2, Node> openSet = new NativeHashMap<int2, Node>(safeGuard, Allocator.Temp);
            NativeHashMap<int2, Node> nodes = new NativeHashMap<int2, Node>(safeGuard, Allocator.Temp);

            Node current = startNative[r];
            current.gScore = 0;
            current.hScore = SquaredDistance(current.coord, end.coord);
            current.fScore = current.gScore + current.hScore;

            openSet.TryAdd(current.coord, current);

            int counter = 0;

            do
            {
                Node result = new Node();
                float fScore = int.MaxValue;

                NativeArray<Node> nodeArray = openSet.GetValueArray(Allocator.Temp);

                for (int i = 0; i < nodeArray.Length; i++)
                {
                    if (nodeArray[i].fScore < fScore)
                    {
                        result = nodeArray[i];
                        fScore = nodeArray[i].fScore;
                    }
                }

                nodeArray.Dispose();

                current = result;
                nodes.TryAdd(current.coord, current);

                for(int i = 0; i < offsets.Length; i++)
                {
                    if (!nodes.ContainsKey(current.coord + offsets[i]) &&
                        !isObstacle.ContainsKey(current.coord + offsets[i]))
                    {
                        Node neighbour = new Node
                        {
                            coord = current.coord + offsets[i],
                            parent = current.coord,
                            gScore = current.gScore +
                                SquaredDistance(current.coord, current.coord + offsets[i]),
                            hScore = SquaredDistance(current.coord + offsets[i], end.coord)
                        };

                        neighbour.fScore = neighbour.gScore + neighbour.hScore;

                        if(openSet.ContainsKey(neighbour.coord) && neighbour.gScore <
                            openSet[neighbour.coord].gScore)
                        {
                            openSet[neighbour.coord] = neighbour;
                        }
                        else if(!openSet.ContainsKey(neighbour.coord))
                        {
                            openSet.TryAdd(neighbour.coord, neighbour);
                        }
                    }
                }

                openSet.Remove(current.coord);
                counter++;

                if (counter > safeGuard)
                    break;

            } while (openSet.Count() != 0 && !current.coord.Equals(end.coord));

            if(nodes.ContainsKey(end.coord))
            {
                int2 currentCoord = end.coord;
                results.Add(startNative[r].coord, end);

                while(!currentCoord.Equals(startNative[r].coord))
                {
                    currentCoord = nodes[currentCoord].parent;
                    results.Add(startNative[r].coord, nodes[currentCoord]);
                }
            }

            openSet.Dispose();
            nodes.Dispose();
        }

        public float SquaredDistance(int2 coordA, int2 coordB)
        {
            float a = coordB.x - coordA.x;
            float b = coordB.y - coordA.y;
            return Mathf.Sqrt(a * a + b * b);
        }
    }
}
