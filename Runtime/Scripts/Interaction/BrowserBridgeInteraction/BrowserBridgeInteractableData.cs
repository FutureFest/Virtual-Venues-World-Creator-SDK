using UnityEngine;

namespace VirtualVenues.WorldCreator
{
    public class BrowserBridgeInteractableData : InteractableData
    {
        [SerializeField][TextArea(minLines: 15, maxLines: 50)] private string _browserBridgePayloadJson = "{\n}";

        public override InteractbaleType InteractableType => InteractbaleType.BrowserBridge;
        public string BrowserBridgePayloadJson => _browserBridgePayloadJson;
    }
}
