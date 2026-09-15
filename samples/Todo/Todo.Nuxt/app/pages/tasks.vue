<script setup lang="ts">
definePageMeta({ middleware: 'auth' })

interface TodoItem {
  id: number
  title: string
  done: boolean
  createdAt: string
}

const { t } = useI18n()
const { data: todos, refresh, pending } = await useTodoApiData<TodoItem[]>('todos', { default: () => [] })
const newTitle = ref('')
const busy = ref(false)

async function add() {
  if (!newTitle.value.trim()) return
  busy.value = true
  try {
    await $todoApi('todos', { method: 'POST', body: { title: newTitle.value.trim() } })
    newTitle.value = ''
    await refresh()
  }
  finally {
    busy.value = false
  }
}

async function toggle(item: TodoItem) {
  await $todoApi(`todos/${item.id}`, { method: 'PUT', body: { title: item.title, done: !item.done } })
  await refresh()
}

async function remove(item: TodoItem) {
  await $todoApi(`todos/${item.id}`, { method: 'DELETE' })
  await refresh()
}
</script>

<template>
  <section class="flex flex-col gap-6">
    <h1 class="text-2xl font-bold tracking-tight">{{ t('tasks.title') }}</h1>

    <form class="flex gap-2" data-testid="add-task" @submit.prevent="add">
      <Input v-model="newTitle" :placeholder="t('tasks.placeholder')" />
      <Button type="submit" :disabled="busy">{{ t('tasks.add') }}</Button>
    </form>

    <Card>
      <CardContent class="pt-6">
        <p v-if="pending" class="text-sm text-muted-foreground">{{ t('tasks.loading') }}</p>
        <p v-else-if="!todos.length" class="text-sm text-muted-foreground">{{ t('tasks.empty') }}</p>
        <ul v-else class="divide-y">
          <li v-for="item in todos" :key="item.id" class="flex items-center gap-3 py-3" :data-testid="`task-${item.id}`">
            <input type="checkbox" :checked="item.done" class="size-4 accent-primary" @change="toggle(item)">
            <span class="flex-1" :class="item.done ? 'text-muted-foreground line-through' : ''">{{ item.title }}</span>
            <Button variant="ghost" size="sm" type="button" @click="remove(item)">{{ t('tasks.delete') }}</Button>
          </li>
        </ul>
      </CardContent>
    </Card>
  </section>
</template>
