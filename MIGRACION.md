# Corte y trazabilidad de migración GL2000

## Configuración por contabilidad

Entre con el usuario de la contabilidad y abra **Utilerías → Corte y lotes de migración → Definir corte**. Seleccione explícitamente el último día del mes cubierto por GL2000. FREDI comienza al día siguiente; no hay un período compartido ni una fecha asignada automáticamente.

- El corte se valida antes de guardar. Las pólizas FREDI anteriores o iguales al corte impiden establecerlo. El mensaje identifica una referencia y la cantidad de conflictos.
- Los acumulados ya existentes posteriores al corte también impiden establecerlo.
- No se eliminan, reclasifican ni ocultan partidas para resolver el conflicto. Revise qué corresponde al sistema antiguo y corrija las capturas de prueba o seleccione el corte real antes de continuar.
- Tras registrar lotes, el corte queda fijo. Cambiarlo requiere una conciliación específica; esta versión no ofrece moverlo automáticamente.
- Las bases antiguas que ya tengan acumulados sin corte deben configurarlo antes de calcular saldos o guardar nuevas pólizas. El catálogo y el listado de pólizas siguen disponibles para la revisión.

## Importación de acumulados

**Utilerías → Importar Catálogo GL2000** registra el archivo, ejercicio, períodos, usuario, fecha UTC, huella del contenido normalizado y una copia de los datos normalizados en un lote. El período 0 identifica la apertura anual. Los archivos originales solo se leen.

El archivo no puede contener actividad posterior al corte: se rechaza en lugar de recortarla sin avisar. Los importes ausentes no se interpretan como cero. Una plantilla sin ejercicio permite importar solo cuentas.

Repetir el mismo contenido (aunque cambie el nombre del archivo o el orden de las cuentas) no agrega importes ni crea otro lote. Si todavía no hay detalle conciliado, un archivo diferente puede reemplazar los acumulados del ejercicio completo: se conserva el lote anterior con el identificador de su sucesor. No se admite reemplazarlo por un catálogo que omita cuentas previamente importadas.

Se conservan también los ceros explícitos del nuevo ejercicio para que no reaparezcan saldos de ejercicios anteriores. Las cuentas superiores mantienen su regla GL2000: se usan sus acumulados propios cuando existen; de lo contrario, se suman sus descendientes.

## Incorporar posteriormente pólizas detalladas

**Corte y lotes de migración → Importar detalle mensual (JSON)** recibe un mes completo en el formato normalizado de abajo. Este paso no interpreta archivos TPS de pólizas: esos archivos necesitan un conversor validado a este formato antes de importarse.

1. Deben existir previamente los acumulados del ejercicio y un corte que incluya el mes.
2. Se validan identificadores, referencias, fechas, cuentas de detalle e igualdad de cargos y créditos de cada póliza.
3. Los cargos y créditos del lote deben coincidir exactamente, en centavos, con los acumulados de cada cuenta. También se concilian las cuentas superiores con acumulados propios; si GL2000 tiene diferencias internas, la importación se detiene para revisarlas.
4. La inserción de pólizas, el lote y la sustitución del período ocurren en una sola transacción. Cualquier error revierte todo.
5. El acumulado se conserva como evidencia, pero el cálculo de ese mes utiliza únicamente las pólizas detalladas. La apertura anual sigue siendo una apertura; no se inventa una póliza para ella.
6. Repetir el mismo detalle devuelve el mismo lote. Otro detalle para un mes ya conciliado se rechaza; no se agrega una segunda vez. Sus pólizas no pueden editarse o borrarse individualmente como capturas locales.
7. Un saldo inicial importado de un ejercicio posterior ya contiene los años anteriores: no se vuelve a sumar el detalle histórico de esos años.

Después de sustituir un mes, el Mayor admite fechas parciales exactas de ese mes para las cuentas de detalle. Si se solicita una cuenta superior excluyendo sus subcuentas, se conserva su acumulado propio; no se inventa detalle directo en esa cuenta.

### Formato del archivo

`DebitCents` y `CreditCents` son **enteros en centavos**: `10000` significa `100.00`. `SourceId` identifica de forma estable la póliza de origen dentro de la contabilidad; incluya el ejercicio si la referencia se repite entre años. Las cuentas deben existir. Ejemplo ilustrativo, que debe ajustarse a los acumulados reales antes de importar:

```json
{
  "Year": 2026,
  "Month": 9,
  "Journals": [
    {
      "SourceId": "GL2000-2026-01-000123",
      "Type": "01",
      "Reference": "000123",
      "Date": "2026-09-15",
      "Concept": "Ingreso histórico",
      "Lines": [
        { "AccountCode": "1.1", "Concept": "Caja", "DebitCents": 10000, "CreditCents": 0 },
        { "AccountCode": "4.1", "Concept": "Ingreso", "DebitCents": 0, "CreditCents": 10000 }
      ]
    }
  ]
}
```

## Consultar procedencia

El Mayor y sus exportaciones identifican movimientos FREDI por póliza, acumulados GL2000 por lote y detalle histórico por lote e identificador de origen. La nota del reporte identifica los lotes relacionados con su apertura.

En **Ver lotes y procedencia** puede consultar el archivo, usuario, ejercicio, meses, huella, lote sucesor y acumulados sustituidos por el detalle. **Exportar evidencia** guarda esos metadatos y el contenido normalizado como JSON en la carpeta de datos de FREDI.

Los importes incorporados por versiones anteriores reciben un lote al establecer el corte, marcado expresamente como **archivo de origen no registrado**. No se inventan el nombre original ni su fecha de importación: la fecha del nuevo lote corresponde al registro de esa evidencia existente.

Esta funcionalidad no configura el corte de ninguna contabilidad real ni importa datos por sí sola. Los cambios reales requieren seleccionar y confirmar el corte y los archivos en la aplicación.
