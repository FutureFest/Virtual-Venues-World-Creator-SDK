using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class TestPlayerSpawner : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private GameObject _testPlayerPrefab = null;

        private void Start()
        {
            var spawnPoints = FindObjectsByType<SpawnPoint>(sortMode: FindObjectsSortMode.None);

            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            if(spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomPoint = UnityEngine.Random.Range(0, spawnPoints.Length);

                Transform spawnPoint = spawnPoints[randomPoint].transform;
                spawnPos = spawnPoint.position;
                spawnRot = spawnPoint.rotation;
            }

            GameObject.Instantiate(_testPlayerPrefab, spawnPos + Vector3.up * 0.1f, spawnRot);
        }
#endif
    }
}
