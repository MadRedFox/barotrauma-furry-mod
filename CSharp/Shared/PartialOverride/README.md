# Partial Item Override System

Этот модуль позволяет частично переопределять предметы из других модов без необходимости копировать всю их конфигурацию.

## Использование

### Инициализация

В точке входа вашего мода (обычно в классе ACsMod):

```csharp
using PartialItemOverride;

namespace YourMod
{
    public class ModEntry : ACsMod
    {
        public ModEntry()
        {
            // Инициализировать систему частичного переопределения
            PartialItemOverrideSystem.Initialize("com.yourname.yourmod");
        }
    }
}
```

### Синтаксис XML

#### Базовый синтаксис

```xml
<Item identifier="item_to_override" inherit="true">
  <!-- Операции переопределения здесь -->
</Item>
```

Атрибут `inherit="true"` активирует систему частичного переопределения.

#### Операция <add>

Добавить новые элементы в существующий узел:

```xml
<Item identifier="combatdivingsuit" inherit="true">
  <add tag="/Wearable">
    <sprite texture="%ModDir%/furry_combatsuit.png" 
            name="Felinid Combat Suit"
            limb="Torso" />
  </add>
</Item>
```

#### Операция <del>

Удалить существующий элемент:

```xml
<Item identifier="combatdivingsuit" inherit="true">
  <del tag="/Fabricate/RequiredItem[identifier=plastic]" />
</Item>
```

#### Операция <modify>

Изменить значение атрибута существующего элемента:

```xml
<Item identifier="combatdivingsuit" inherit="true">
  <modify tag="/Wearable/sprite[name='Combat Suit']">
    <update property="hidelimb" value="false" />
  </modify>
</Item>
```

Можно изменить несколько свойств одного элемента за раз:

```xml
<Item identifier="combatdivingsuit" inherit="true">
  <modify tag="/Wearable/damagemodifier[afflictiontypes='bleeding']">
    <update property="damagemultiplier" value="0.30" />
    <update property="deflectprojectiles" value="true" />
    <update property="damagesound" value="LimbArmor" />
  </modify>
</Item>
```

### Синтаксис пути (XPath-подобный)

#### Простой путь
```
/Element/SubElement/DeepElement
```

#### С предикатами (фильтры)
```
/Wearable/sprite[name='Combat Armor']
/Fabricate/RequiredItem[identifier=plastic]
/Price[storeidentifier=merchantmilitary]
```

#### Множественные предикаты
```
/sprite[name='Heavy Armor',limb=Torso]
```

## Примеры

### Добавление вариантов текстур для разных видов

```xml
<Item identifier="ek_merc_armor_uniform" inherit="true">
  <add tag="/Wearable">
    <sprite name="Mercenary Armor Felinid" 
            texture="%ModDir%/armor_felinid.png"
            limb="Torso"
            speciesname="felinid" />
  </add>
</Item>
```

### Изменение значений брони

```xml
<Item identifier="combatdivingsuit" inherit="true">
  <modify tag="/Wearable/damagemodifier[afflictiontypes=bleeding]">
    <update property="damagemultiplier" value="0.3" />
  </modify>
  <modify tag="/Wearable/damagemodifier[afflictionidentifiers=gunshotwound]">
    <update property="damagemultiplier" value="0.25" />
  </modify>
</Item>
```

### Удаление требований к крафту

```xml
<Item identifier="expensive_item" inherit="true">
  <del tag="/Fabricate/RequiredItem[identifier=titaniumaluminiumalloy]" />
</Item>
```

### Комплексный пример

```xml
<Item identifier="ek_merc_armor_uniform" inherit="true">
  <!-- Добавить условный спрайт для обуви -->
  <add tag="/Wearable/sprite[name='Mercenary Combat Undershirt Right Shoe']">
    <conditionalSprite target="person">
      <conditional hasSpecifiertag="furry" />
      <properties>
        <property name="hidelimb">false</property>
      </properties>
    </conditionalSprite>
  </add>
  
  <!-- Улучшить защиту (несколько свойств одновременно!) -->
  <modify tag="/Wearable/damagemodifier[afflictiontypes=bleeding]">
    <update property="damagemultiplier" value="0.8" />
    <update property="deflectprojectiles" value="true" />
  </modify>
  
  <!-- Удалить один из материалов для крафта -->
  <del tag="/Fabricate/RequiredItem[identifier=plastic]" />
</Item>
```

## Преимущества

✅ **Меньше кода**: Вместо 80-120 строк на предмет - только 5-20 строк изменений  
✅ **Автообновление**: Изменения в базовом моде автоматически применяются  
✅ **Совместимость**: Несколько модов могут изменять один предмет  
✅ **Читаемость**: Сразу видно, что именно изменено  

## Технические детали

### Архитектура

1. `PartialItemOverrideSystem` - Основная точка входа
2. `XPathParser` - Парсер путей XPath-подобного синтаксиса
3. `ItemPrefabPatches` - Harmony-патчи для перехвата загрузки предметов

### Поддерживаемые операции

- `<add tag="/path">` - Добавить дочерние элементы
- `<del tag="/path">` - Удалить элемент
- `<modify tag="/path">` - Изменить атрибуты элемента
  - Внутри: `<update property="..." value="..." />` - Изменить свойство
  - Можно добавить несколько `<update>` для изменения множества свойств

### Ограничения

- Путь должен существовать в базовом предмете
- Предикаты сопоставляются с учётом регистра (case-insensitive)
- Операция `<modify>` требует хотя бы один `<update>` дочерний элемент

## Отладка

Система выводит подробные логи в консоль:
- `[PartialOverride] ...` - Информационные сообщения
- Зелёные сообщения - Успешная инициализация
- Красные сообщения - Ошибки

## Экспорт в отдельный мод

Эта система полностью независима и может быть извлечена в отдельный мод-библиотеку:

1. Скопируйте папку `PartialOverride/` целиком
2. Создайте новый мод с filelist.xml
3. Другие моды могут ссылаться на него как зависимость

```xml
<!-- В filelist.xml вашего мода -->
<contentpackage name="Partial Item Override System" ...>
  <csharpfile file="PartialOverride/PartialItemOverrideSystem.cs" />
  <csharpfile file="PartialOverride/XPathParser.cs" />
  <csharpfile file="PartialOverride/ItemPrefabPatches.cs" />
</contentpackage>
```
