using System;
using Artech.Architecture.Common.Services;

namespace GxCodeKeys
{
    /// <summary>Errores a la ventana Output de GeneXus, sin interrumpir al usuario.</summary>
    static class Log
    {
        public static void Error(string operation, Exception ex)
        {
            try
            {
                CommonServices.Output.AddWarningLine("GxCodeKeys (" + operation + "): " + ex.Message);
            }
            catch (Exception)
            {
                // Sin Output (IDE cerrándose): no hay dónde informarlo
            }
        }
    }
}
