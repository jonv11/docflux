## Embedded Looking Fences

Prose before the first code block.

```python
def render():
    text = "~~~ not a fence here"
    return "``` also not closing because this is code"
```

Prose between code blocks.

````
```
fake nested block text
```
````

Closing prose after all blocks.