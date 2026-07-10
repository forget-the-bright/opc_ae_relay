using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace opcLearn.config
{
    public static class Config
    {
        public static void initAll() {
            LogConfig.init();
            AlarmConfigLoader.init();
        }
    }
}
