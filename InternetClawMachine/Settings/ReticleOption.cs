namespace InternetClawMachine.Settings
{
    /// <summary>
    /// Reticle settings
    /// </summary>
    public class ReticleOption
    {
        /// <summary>
        /// OBS Source name to update
        /// </summary>
        public string ClipName { set; get; }

        /// <summary>
        /// What value is matched for redemption purposes
        /// </summary>
        public string RedemptionName { set; get; }

        /// <summary>
        /// Path to the reticle
        /// </summary>
        public string FilePath { set; get; }
    }
}