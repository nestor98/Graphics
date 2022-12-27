using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

using UnityEngine; // GameObject

using UnityEditor;


namespace UnityEngine.Rendering.HighDefinition
{
    
    public partial class HDRenderPipeline
    {
        List<LocalVolumetricFog> m_userFogs;
        LocalVolumetricFog m_oceanFog = null;

        internal bool m_oceanFogIsSet = false;
        public HDRPManager m_hdrpManager; // TODO: usar el componente HDRPManager para no crear esto cada vez


        public void SetUpOceanFog() {
            // Debug.Log("Setting up Ocean Fog (this should only happen once)");

            m_userFogs = GetAllFogInScene();
            m_oceanFog = UWGetLocalVolumetricFog();
            // Debug.Log("Found " + m_userFogs.Count + " fogs");
        }

        public void UWSetOceanFog() {
            if (!m_oceanFogIsSet) {

                if (m_oceanFog == null) {
                    if (oceanData == null) return; // We need the ocean, wait for it
                    SetUpOceanFog();
                }
                
                for (int i=0; i<m_userFogs.Count; i++) {
                    m_userFogs[i].enabled=false;
                }
                m_oceanFog.enabled=true;

                m_oceanFogIsSet = true;
            }
        }

        public void UWSetUserFogs() {
            if (m_oceanFogIsSet) {      
                foreach (var vol in m_userFogs) {
                    vol.enabled = true;
                }
                m_oceanFog.enabled = false;

                m_oceanFogIsSet = false;
            }
        }

        // Returns a list of the LocalVolumetricFog components in the scene
        List<LocalVolumetricFog> GetAllFogInScene()
        {
            List<LocalVolumetricFog> objectsInScene = new List<LocalVolumetricFog>();

            foreach (LocalVolumetricFog go in Resources.FindObjectsOfTypeAll(typeof(LocalVolumetricFog)) as LocalVolumetricFog[])
            {
#if UNITY_EDITOR
                if (!EditorUtility.IsPersistent(go.transform.root.gameObject) && !(go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave))
                    objectsInScene.Add(go);
#else
                objectsInScene.Add(go); 
#endif
            }
            
            return objectsInScene;
        }


        private LocalVolumetricFog UWGetLocalVolumetricFog() {
            return m_hdrpManager.UpdateFog(); 
        }


    }
}


